using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">Every Report, quick or taking longer; one whose
/// Quest probes a configuration, opened.</para>
/// </summary>
/// <remarks>
/// A Report presents existing data; a Quest probes for data to present in a
/// Report. Every Report is in one list, and each says which (ADR-0072,
/// amendment 2026-11-10): Quick, it presents data that already exists and
/// is opened at the cmdlet that presents it, its PresentedBy — or Takes
/// longer, it presents what a Quest probes first, and says how long its last
/// probe took. One built later is listed and says not yet available. One
/// noun for every Report, the act a parameter on it: -Configuration opens a
/// Report that presents what a Quest probes, its Quest probing a node's
/// configuration — or a cluster's cluster.xmip, sliced for -Node — and
/// changing nothing. The list, the words, the one entry -Name names, what a
/// probe finds and every refusal are the runtime's
/// (<see cref="ReportCatalog"/>, the one <c>xmip report</c> and the GUI's
/// Reports view read). A probe is audited where this module's records go,
/// and the list reads each Quest's last probe back from there; where it
/// cannot, every Report is listed all the same and the runtime's sentence is
/// written as a warning. Objects out: <see cref="ReportEntry"/>; opened,
/// <see cref="ReportProbed"/>, its Tally the <see cref="ReportCount"/>
/// objects and its Rows the <see cref="ReportRow"/> objects, or with -Row
/// the <see cref="ReportRequirement"/> in full.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipReport", DefaultParameterSetName = ListSet)]
[OutputType(typeof(ReportEntry), ParameterSetName = [ListSet])]
[OutputType(typeof(ReportProbed), typeof(ReportRequirement), ParameterSetName = [OpenSet])]
public sealed class GetXmipReportCommand : XmipCommand
{
    private const string ListSet = "List";
    private const string OpenSet = "Open";

    // This surface's cmdlet for each reader of the header a quick Report
    // names: the one place a quick entry is opened.
    private static readonly Dictionary<string, string> Presenting = new(StringComparer.Ordinal)
    {
        [OperateAbi.AuditReadEntrypoint] = Named<GetXmipAuditCommand>(),
        [OperateAbi.DeadMessagesEntrypoint] = Named<GetXmipDeadMessageCommand>(),
        [OperateAbi.SubscriptionsEntrypoint] = Named<GetXmipSubscriptionCommand>(),
        [OperateAbi.PublicationReadEntrypoint] = Named<GetXmipScopeCommand>(),
    };

    private RuntimeReports? _reports;

    /// <summary>
    /// <para type="description">The Report, by its name in the list; every
    /// one when omitted. Tab offers the names the runtime lists.</para>
    /// </summary>
    [Parameter(Position = 0, ParameterSetName = ListSet)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = OpenSet)]
    [ArgumentCompleter(typeof(ReportWords))]
    public string? Name { get; set; }

    /// <summary>
    /// <para type="description">What the Report's Quest probes: a node's
    /// configuration, or a cluster's cluster.xmip with -Node. Told apart by
    /// what the file holds, never by its name. Read, never changed.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = OpenSet)]
    public string Configuration { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The node a cluster's cluster.xmip is sliced
    /// for.</para>
    /// </summary>
    [Parameter(ParameterSetName = OpenSet)]
    public string? Node { get; set; }

    /// <summary>
    /// <para type="description">What the Report is opened for, one of the
    /// choices its entry names: the regulation, GDPR. Tab offers
    /// them.</para>
    /// </summary>
    [Parameter(ParameterSetName = OpenSet)]
    [ArgumentCompleter(typeof(ReportWords))]
    public string? Parameter { get; set; }

    /// <summary>
    /// <para type="description">One row of the Report's second level, by
    /// its name, in full — a requirement, what is behind it and the actions
    /// that close it. A blank one is refused.</para>
    /// </summary>
    [Parameter(ParameterSetName = OpenSet)]
    [ValidateNotNullOrWhiteSpace]
    public string? Row { get; set; }

    /// <summary>
    /// <para type="description">The runtime's native library to ask.
    /// Omitted, it is found by the one rule: RuntimeLibrary in the module's
    /// document, else XMIP_RUNTIME_LIBRARY, else beside the module.</para>
    /// </summary>
    [Parameter]
    public string? Library { get; set; }

    /// <inheritdoc />
    protected override void Begin()
    {
        string? library = string.IsNullOrWhiteSpace(Library)
            ? null
            : GetUnresolvedProviderPathFromPSPath(Library);
        _ = ModuleSurface.Runtime(library);

        try
        {
            _reports = RuntimeLibrary.Rules.Reports;
        }
        catch (InvalidOperationException unloaded)
        {
            Stop(new ErrorRecord(
                unloaded, "XmipRuntimeUnloadable", ErrorCategory.ResourceUnavailable, Library));
        }
    }

    /// <inheritdoc />
    protected override void Process()
    {
        try
        {
            if (ParameterSetName == OpenSet)
            {
                ReportProbe probe = new(
                    Name ?? string.Empty, GetUnresolvedProviderPathFromPSPath(Configuration))
                {
                    Parameter = Parameter,
                    Row = Row,
                    Node = Node,
                };
                ReportProbed probed = ReportCatalog.Open(_reports!, probe, ModuleAudit.Open());
                WriteObject(Row is null ? probed : probed.Requirement);
                return;
            }

            ReportListing listing = Name is null
                ? ReportCatalog.List(_reports!, ModuleAudit.Open())
                : ReportCatalog.Named(_reports!, ModuleAudit.Open(), Name);

            if (listing.Unread is { } unread)
            {
                WriteWarning(unread);
            }

            WriteObject(listing.Entries.Select(Presented), enumerateCollection: true);
        }
        catch (InvalidOperationException refused)
        {
            Refuse(new ErrorRecord(
                refused, "XmipReportRefused", ErrorCategory.InvalidArgument, Name));
        }
    }

    // The entry, with this surface's cmdlet that presents it where it is a
    // quick one: PresentedBy, null otherwise.
    private static PSObject Presented(ReportEntry entry)
    {
        PSObject shown = PSObject.AsPSObject(entry);
        string? presenting = entry.ReadBy is { } reader
            ? Presenting.GetValueOrDefault(reader)
            : null;

        shown.Properties.Add(new PSNoteProperty("PresentedBy", presenting));
        return shown;
    }

    private static string Named<T>()
        where T : Cmdlet
    {
        CmdletAttribute named = typeof(T).GetCustomAttribute<CmdletAttribute>()
            ?? throw new InvalidOperationException($"{typeof(T).Name} is no cmdlet");

        return $"{named.VerbName}-{named.NounName}";
    }
}

/// <summary>
/// What Tab offers for <c>-Name</c> and <c>-Parameter</c>: the names the
/// runtime lists, and the choices of the Report <c>-Name</c> names — each
/// Report's where none is named — so no word list is kept here. Nothing when
/// the runtime cannot answer.
/// </summary>
public sealed class ReportWords : IArgumentCompleter
{
    /// <inheritdoc />
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        CommandAst commandAst,
        IDictionary fakeBoundParameters)
    {
        IEnumerable<string> words;

        try
        {
            object? named = fakeBoundParameters?["Name"];
            ReportListing listing = parameterName == "Parameter" && named is string name
                ? ReportCatalog.Named(ModuleAudit.Open(), name)
                : ReportCatalog.List(ModuleAudit.Open());

            words = parameterName == "Parameter"
                ? listing.Entries
                    .SelectMany(entry => entry.Parameter?.Choices ?? [])
                    .Distinct(StringComparer.Ordinal)
                : listing.Entries.Select(entry => entry.Name);
        }
        catch (InvalidOperationException)
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
