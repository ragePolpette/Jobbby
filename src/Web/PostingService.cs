using ApplicationLedger;
using Config;
using CvExtraction;
using GraphEngine;
using Host;
using JobPostings;

namespace Web;

/// <summary>
/// A posting the UI acts on: the ledger's current record, or a posting stored in a dry run's file
/// (dry runs never write the ledger).
/// </summary>
public abstract record PostingTarget
{
    public sealed record Ledger(string PostingId) : PostingTarget;
    public sealed record Run(string RunId, string PostingId) : PostingTarget;
}

/// <summary>Why a posting action could not happen, with the HTTP status it maps to.</summary>
public sealed class PostingActionException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>Presentation messages and pasted full texts, on ledger and dry-run postings alike.</summary>
public sealed class PostingService(
    DataDir dataDir,
    RunStore runs,
    LedgerHolder ledgers,
    SettingsService settingsService,
    RunManager runManager,
    IRunDependenciesFactory dependencies,
    IConfiguration configuration)
{
    public ApplicationRecord Get(PostingTarget target)
    {
        switch (target)
        {
            case PostingTarget.Ledger ledger:
                return Ledger().Current(ledger.PostingId) ?? throw NotFound();
            case PostingTarget.Run run:
                return RunPosting(run).Record ?? throw new PostingActionException(StatusCodes.Status400BadRequest, "Questo annuncio non è stato valutato.");
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    public ApplicationRecord Save(PostingTarget target, Func<ApplicationRecord, ApplicationRecord> change)
    {
        switch (target)
        {
            case PostingTarget.Ledger ledger:
                return Ledger().Update(ledger.PostingId, change) ?? throw NotFound();
            case PostingTarget.Run run:
                EnsureWritable(run);
                var updated = runs.UpdatePosting(run.RunId, run.PostingId, posting =>
                    posting.Record is null ? posting : posting with { Record = change(posting.Record) with { PostingId = posting.Record.PostingId, DedupeKey = posting.Record.DedupeKey } });
                return updated?.Record ?? throw NotFound();
            default:
                throw new ArgumentOutOfRangeException(nameof(target));
        }
    }

    public async Task<ApplicationRecord> GeneratePresentationAsync(PostingTarget target, CancellationToken cancellationToken)
    {
        if (target is PostingTarget.Run run)
            EnsureWritable(run);
        var record = Get(target);
        var settings = settingsService.Load();
        var llm = CreateLlm(settings);
        var message = await new PresentationWriter(llm).WriteAsync(record, await LoadCvAsync(llm, cancellationToken), settings.Presentation, cancellationToken);
        return Save(target, current => current with { Presentation = message });
    }

    public ApplicationRecord EditPresentation(PostingTarget target, string? text)
    {
        var trimmed = text?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            throw new PostingActionException(StatusCodes.Status400BadRequest, "Il messaggio non può essere vuoto.");
        if (trimmed.Length > UserFullText.MaxLength)
            throw new PostingActionException(StatusCodes.Status400BadRequest, $"Il messaggio supera i {UserFullText.MaxLength} caratteri.");

        return Save(target, current => current with
        {
            Presentation = current.Presentation is { } existing
                ? existing with { Text = trimmed, Edited = true }
                : new PresentationMessage(trimmed, DateTimeOffset.UtcNow, Edited: true, BasedOnFullText: current.FullText is not null, new List<string>()),
        });
    }

    public async Task<ApplicationRecord> SetFullTextAsync(PostingTarget target, string? text, CancellationToken cancellationToken)
    {
        var sweep = SearchSweep.Local;
        if (target is PostingTarget.Run run)
        {
            EnsureWritable(run);
            Enum.TryParse(RunPosting(run).Sweep, out sweep);
        }

        var record = Get(target);

        var settings = settingsService.Load();
        var llm = CreateLlm(settings);
        var cv = await LoadCvAsync(llm, cancellationToken);
        ApplicationRecord reevaluated;
        try
        {
            reevaluated = await new PostingReevaluator(llm).ReevaluateAsync(record, text ?? string.Empty, cv, settings, SkillAliases.Load(dataDir.SkillAliasesPath), sweep, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new PostingActionException(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (Exception ex) when (ex is LlmException or System.Text.Json.JsonException or InvalidOperationException)
        {
            throw new PostingActionException(StatusCodes.Status502BadGateway, $"Rivalutazione non riuscita: {ex.Message}");
        }

        return Save(target, _ => reevaluated);
    }

    /// <summary>A message with settings not saved yet, on the given posting or the most recent evaluated one.</summary>
    public async Task<(PresentationMessage Message, ApplicationRecord Posting)> PreviewAsync(PresentationSettings presentation, PostingTarget? target, CancellationToken cancellationToken)
    {
        var posting = target is null ? SamplePosting() : Get(target);
        var settings = settingsService.Load();
        var llm = CreateLlm(settings);
        var message = await new PresentationWriter(llm).WriteAsync(posting, await LoadCvAsync(llm, cancellationToken), presentation, cancellationToken);
        return (message, posting);
    }

    private ApplicationRecord SamplePosting() =>
        Ledger().Recent(50).FirstOrDefault(record => record.Outcome != ApplicationOutcomes.AutoRejected && !string.IsNullOrWhiteSpace(record.Excerpt))
        ?? runs.List().SelectMany(run => run.Postings).Select(posting => posting.Record)
            .FirstOrDefault(record => record is not null && record.Outcome != ApplicationOutcomes.AutoRejected)
        ?? throw new PostingActionException(StatusCodes.Status400BadRequest, "Nessun annuncio valutato su cui provare il messaggio: fai prima una run.");

    private ApplicationLedger.ApplicationLedger Ledger() => ledgers.Get(settingsService.Load().Dedupe.ExtraCompanySuffixes);

    private RunPosting RunPosting(PostingTarget.Run run) =>
        runs.Load(run.RunId)?.Postings.FirstOrDefault(posting => posting.PostingId == run.PostingId) ?? throw NotFound();

    /// <summary>A run still going writes its own file at the end: editing it now would be overwritten.</summary>
    private void EnsureWritable(PostingTarget.Run run)
    {
        if (runManager.Current is { Finished: false } active && active.RunId == run.RunId)
            throw new PostingActionException(StatusCodes.Status409Conflict, "La run è ancora in corso: riprova quando è finita.");
    }

    private ILlmClient CreateLlm(JobbbySettings settings)
    {
        try
        {
            return dependencies.CreateLlm(settings);
        }
        catch (InvalidOperationException ex)
        {
            throw new PostingActionException(StatusCodes.Status400BadRequest, ex.Message);
        }
    }

    private async Task<CvData> LoadCvAsync(ILlmClient llm, CancellationToken cancellationToken)
    {
        var path = CvLocator.Find(dataDir, configuration)
            ?? throw new PostingActionException(StatusCodes.Status400BadRequest, CvLocator.NotFoundMessage(dataDir));
        try
        {
            return await CvLoader.LoadAsync(path, llm, cancellationToken);
        }
        catch (Exception ex) when (ex is LlmException or System.Text.Json.JsonException or InvalidOperationException)
        {
            // Only a PDF needs the LLM here: extract it from the CV page to avoid this step.
            throw new PostingActionException(StatusCodes.Status502BadGateway, $"CV non letto: {ex.Message}");
        }
    }

    private static PostingActionException NotFound() => new(StatusCodes.Status404NotFound, "Annuncio sconosciuto.");
}
