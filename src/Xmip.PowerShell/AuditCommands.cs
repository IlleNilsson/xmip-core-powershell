using System.Collections;
using System.Globalization;
using System.Management.Automation;
using System.Management.Automation.Language;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">What the audit recorded: every Xmip program's
/// records, newest first.</para>
/// </summary>
/// <remarks>
/// The audit read back by the audit capability's one reader
/// (<see cref="ProgramAudit.Read"/>, ADR-0062, amendment 2026-09-29) — the one
/// <c>xmip-cli audit</c> and the web's Audit view call — from where this
/// module's own records go: <c>AuditDirectory</c> in
/// <c>xmip.powershell.toml</c>, else <c>XMIP_AUDIT_DIRECTORY</c>. Each
/// parameter is a word of the query, and what it means, which records match
/// and in what order is the capability's. The words a severity, a sort and an
/// action take are the capability's too, offered on Tab from what it answers
/// rather than listed here. Objects out: the <see cref="AuditEntry"/> itself.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipAudit", SupportsPaging = true)]
[OutputType(typeof(AuditEntry))]
public sealed class GetXmipAuditCommand : XmipCommand
{
    /// <summary>
    /// <para type="description">A scope pattern over the location each
    /// record's process declared, * and ? — xmip:///C1/* is everything in
    /// cluster C1. A record with no location is at the root, which only *
    /// names.</para>
    /// </summary>
    [Parameter(Position = 0)]
    [SupportsWildcards]
    public string? Pattern { get; set; }

    /// <summary>
    /// <para type="description">The records whose process declared this
    /// scope, or one beneath it, such as xmip:///C1.</para>
    /// </summary>
    [Parameter]
    public string? Location { get; set; }

    /// <summary>
    /// <para type="description">The records of programs that declared no
    /// location, on this machine: the query's host.</para>
    /// </summary>
    [Parameter]
    public string? ComputerName { get; set; }

    /// <summary>
    /// <para type="description">The records of one program, exactly, such
    /// as xmip-cli or Xmip.PowerShell.</para>
    /// </summary>
    [Parameter]
    public string? Program { get; set; }

    /// <summary>
    /// <para type="description">The one record with this identifier; every
    /// other filter is set aside.</para>
    /// </summary>
    [Parameter(ValueFromPipelineByPropertyName = true)]
    public string? AuditId { get; set; }

    /// <summary>
    /// <para type="description">information, warning or error.</para>
    /// </summary>
    [Parameter]
    [ArgumentCompleter(typeof(AuditWords))]
    public string? Severity { get; set; }

    /// <summary>
    /// <para type="description">One action, exactly: a cmdlet's name, a
    /// command's word, start or stop.</para>
    /// </summary>
    [Parameter]
    [ArgumentCompleter(typeof(AuditWords))]
    public string? Action { get; set; }

    /// <summary>
    /// <para type="description">At or after this time. A time with no kind,
    /// such as [datetime]'2026-09-29', is UTC, as the capability reads a time
    /// with no zone; Get-Date's local time is converted.</para>
    /// </summary>
    [Parameter]
    public DateTime? From { get; set; }

    /// <summary>
    /// <para type="description">At or before this time, as From.</para>
    /// </summary>
    [Parameter]
    public DateTime? To { get; set; }

    /// <summary>
    /// <para type="description">The column to sort by: at, location, node,
    /// program, host, action, phase, severity or summary. Omitted, at.</para>
    /// </summary>
    [Parameter]
    [ArgumentCompleter(typeof(AuditWords))]
    public string? Sort { get; set; }

    /// <summary>
    /// <para type="description">Oldest, or least, first. Omitted, newest
    /// first.</para>
    /// </summary>
    [Parameter]
    public SwitchParameter Ascending { get; set; }

    /// <summary>What this invocation asks, in the query's words. -First is the
    /// page's length, the capability's 100 when omitted and 1000 at most;
    /// -Skip where it starts.</summary>
    public AuditQuery Query()
    {
        return new AuditQuery
        {
            Pattern = Pattern,
            Location = Location,
            Host = ComputerName,
            Program = Program,
            Record = AuditId,
            Severity = Severity,
            Action = Action,
            From = Moment(From),
            To = Moment(To),
            Sort = Sort,
            Order = Ascending ? "ascending" : null,
            Offset = (int)Math.Min(PagingParameters.Skip, int.MaxValue),
            Limit = PagingParameters.First == ulong.MaxValue
                ? 0
                : (int)Math.Min(PagingParameters.First, int.MaxValue),
        };
    }

    /// <summary>A [datetime] as the query's RFC 3339: one of no kind as
    /// written, which the capability reads as UTC; a local or UTC one in
    /// UTC.</summary>
    public static string? Moment(DateTime? time)
    {
        return time switch
        {
            null => null,
            { Kind: DateTimeKind.Unspecified } unzoned =>
                unzoned.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff", CultureInfo.InvariantCulture),
            { } zoned => zoned.ToUniversalTime()
                .ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        };
    }

    /// <inheritdoc />
    protected override void Process()
    {
        AuditQuery query = Query();
        AuditRead read;

        try
        {
            read = ModuleAudit.Open().Read(query);
        }
        catch (ArgumentException refused)
        {
            Stop(new ErrorRecord(
                new ArgumentException(English.Refusal(refused)),
                "XmipAuditQueryRefused",
                ErrorCategory.InvalidArgument,
                query));

            return;
        }
        catch (Exception unread) when (unread is IOException or InvalidOperationException)
        {
            Stop(new ErrorRecord(unread, "XmipAuditUnreadable", ErrorCategory.ReadError, query));

            return;
        }

        if (read.File.Length == 0)
        {
            Stop(new ErrorRecord(
                new InvalidOperationException(English.NoAuditDirectory()),
                "XmipAuditDirectoryUnstated",
                ErrorCategory.ResourceUnavailable,
                query));

            return;
        }

        if (PagingParameters.IncludeTotalCount)
        {
            WriteObject(PagingParameters.NewTotalCount((ulong)read.Matched, 1.0));
        }

        if (AuditId is { Length: > 0 } id && read.Records.Count == 0)
        {
            Refuse(new ErrorRecord(
                new ItemNotFoundException($"No record {id} in {read.File}."),
                "XmipAuditRecordNotFound",
                ErrorCategory.ObjectNotFound,
                id));

            return;
        }

        foreach (AuditEntry entry in read.Records)
        {
            WriteObject(entry);
        }
    }
}

/// <summary>
/// What Tab offers for <c>-Severity</c>, <c>-Sort</c> and <c>-Action</c>: the
/// words the audit capability answers with — its severities, its columns, and
/// the actions recorded where the module's audit is — so no word list is kept
/// here. Nothing when the audit cannot be read.
/// </summary>
public sealed class AuditWords : IArgumentCompleter
{
    /// <inheritdoc />
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters)
    {
        IReadOnlyList<string> words;

        try
        {
            AuditRead read = ModuleAudit.Open().Read(new AuditQuery { Limit = 1 });
            words = parameterName switch
            {
                "Severity" => read.Severities,
                "Sort" => read.Columns,
                _ => read.Actions,
            };
        }
        catch (Exception unread) when (
            unread is ArgumentException or IOException or InvalidOperationException)
        {
            words = [];
        }

        string typed = (wordToComplete ?? string.Empty).Trim('\'', '"');

        return words
            .Where(word => word.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Select(word => new CompletionResult(
                word.Contains(' ', StringComparison.Ordinal) ? $"'{word}'" : word,
                word,
                CompletionResultType.ParameterValue,
                word));
    }
}
