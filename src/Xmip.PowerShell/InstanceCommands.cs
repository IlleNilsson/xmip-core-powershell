using System.Management.Automation;
using Xmip.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">The Instances of each Work Process, one opened with
/// its history of Messages, and the Messages that failed in its
/// queue.</para>
/// </summary>
/// <remarks>
/// One cmdlet for the noun (runtime-model.md section 22; ADR-0066, amendment
/// 2026-11-25), as <c>xmip instances</c> takes it: each Instance running,
/// waiting for a Message of its key until its deadline, failed with its code
/// and why, dismissed or completed, with its own values and turns. An
/// Instance takes no act of its own: a Message that failed at a Work Process
/// is retried or dismissed as every failed Message is, by
/// <c>Get-XmipScope -Scope &lt;node&gt; -Journey &lt;id&gt; -Retry</c> or
/// <c>-Dismiss</c>, its Journey the record's <c>FailedBy</c> or, with
/// <c>-FailedMessage</c>, each failed Message's <c>Journey</c>. Which a call
/// selects is <see cref="WorkProcessInstanceQuery"/>'s, the one every .NET
/// surface asks. Objects out: <see cref="WorkProcessInstanceRecord"/>, opened
/// with <c>Format-List</c>, or <see cref="FailedMessageRecord"/>.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipInstance", DefaultParameterSetName = ListSet)]
[OutputType(typeof(WorkProcessInstanceRecord), ParameterSetName = [ListSet])]
[OutputType(typeof(FailedMessageRecord), ParameterSetName = [FailedSet])]
public sealed class GetXmipInstanceCommand : XmipSurfaceCommand
{
    private const string ListSet = "List";
    private const string FailedSet = "Failed";

    /// <summary>
    /// <para type="description">One Work Process, by its configured name.
    /// Omitted, every one.</para>
    /// </summary>
    [Parameter(Position = 0)]
    public string? WorkProcess { get; set; }

    /// <summary>
    /// <para type="description">Where the drill stands: a cluster, such as
    /// xmip:///&lt;cluster&gt;, or a node, such as
    /// xmip:///&lt;cluster&gt;/node/&lt;node&gt;, and every Work Process run at
    /// or beneath it.</para>
    /// </summary>
    [Parameter]
    [Alias("Node")]
    public string? Location { get; set; }

    /// <summary>
    /// <para type="description">One Instance, by its identifier: opened with
    /// Format-List, its values and its history of Messages.</para>
    /// </summary>
    [Parameter(ParameterSetName = ListSet)]
    public string? Instance { get; set; }

    /// <summary>
    /// <para type="description">The Messages that failed in each Work
    /// Process's queue, oldest first, each with why: the Journey Get-XmipScope
    /// -Journey retries or dismisses on the node. How many wait in each queue
    /// is said with -Verbose.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = FailedSet)]
    public SwitchParameter FailedMessage { get; set; }

    /// <summary>What this invocation asks, in the query's words.</summary>
    public WorkProcessInstanceQuery Query()
    {
        return new WorkProcessInstanceQuery
        {
            Location = Location,
            WorkProcess = WorkProcess,
            Instance = Instance,
        };
    }

    /// <inheritdoc />
    protected override void Process()
    {
        IReadOnlyList<WorkProcessInstances> chosen =
            Query().Apply(Surface.WorkProcessInstances());

        foreach (WorkProcessInstances process in chosen)
        {
            WriteVerbose(
                $"{process.Node} Work Process {process.WorkProcess}: "
                + $"{process.Instances.Count} Instance(s), {process.FailedCount} failed in its "
                + "queue");

            if (FailedMessage)
            {
                foreach (FailedMessageRecord failed in process.Failed)
                {
                    WriteObject(failed);
                }

                continue;
            }

            foreach (WorkProcessInstanceRecord one in process.Instances)
            {
                WriteObject(one);
            }
        }

        if (!FailedMessage && Instance is { Length: > 0 } && chosen.Count == 0)
        {
            Refuse(new ErrorRecord(
                new ItemNotFoundException($"REFUSED: no Instance {Instance} is listed."),
                "XmipInstanceNotFound",
                ErrorCategory.ObjectNotFound,
                Instance));
        }
    }
}
