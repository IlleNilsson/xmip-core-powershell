using System.Management.Automation;
using Xmip.Abi.Module;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

// The two acts the operator boundary carries (ADR-0027 clause 5), as cmdlets.
// Suspend rather than Pause because Pause is not an approved verb. Each emits
// the ScopeOperation the cli renders, so a pipeline object and an exit code
// say the same thing. There is no Start, Stop or Restart: the boundary has no
// such call, and the thing that watches must not be able to stop the thing it
// watches.

/// <summary>What the two scope cmdlets share: the runtime, the scopes, the act.</summary>
public abstract class XmipScopeCommand : PSCmdlet
{
    private Operator? _runtime;

    /// <summary>
    /// <para type="description">Path to the runtime's native library, the one
    /// exporting xmip_operate_v1.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    public string Library { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The Xmip URI to act on, such as
    /// xmip:///edge-01. Everything beneath it is included.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 1, ValueFromPipeline = true)]
    public string[] Scope { get; set; } = [];

    /// <summary>The act, and how ShouldProcess names it.</summary>
    protected abstract ScopeAction Action { get; }

    /// <summary>The runtime's answer for one scope.</summary>
    protected abstract XmipStatus Apply(Operator runtime, string scope);

    /// <summary>The answer in English, for the operator to see.</summary>
    protected abstract string Said(string scope, XmipStatus status);

    /// <inheritdoc />
    protected override void BeginProcessing()
    {
        _runtime = GetXmipHealthCommand.LoadRuntime(this, Library);
    }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        foreach (string scope in Scope)
        {
            if (!ShouldProcess(scope, Action.ToString()))
            {
                continue;
            }

            XmipStatus status = Apply(_runtime!, scope);
            ScopeOperation operation = new(
                scope, Action, status == XmipStatus.Ok, Said(scope, status));

            if (!operation.Applied)
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException(operation.Result),
                    "XmipScopeOperationRefused",
                    ErrorCategory.InvalidOperation,
                    scope));

                continue;
            }

            WriteObject(operation);
        }
    }

    /// <inheritdoc />
    protected override void EndProcessing()
    {
        _runtime?.Dispose();
    }

    /// <inheritdoc />
    protected override void StopProcessing()
    {
        _runtime?.Dispose();
    }
}

/// <summary>
/// <para type="synopsis">Pause everything at and beneath a scope.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Suspend, "XmipScope", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class SuspendXmipScopeCommand : XmipScopeCommand
{
    /// <summary>
    /// <para type="description">Who is pausing; the runtime records it as the
    /// evidence of the Paused mood. The current user when omitted.</para>
    /// </summary>
    [Parameter]
    public string Who { get; set; } = Environment.UserName;

    /// <inheritdoc />
    protected override ScopeAction Action => ScopeAction.Pause;

    /// <inheritdoc />
    protected override XmipStatus Apply(Operator runtime, string scope)
    {
        return runtime.PauseScope(scope, Who);
    }

    /// <inheritdoc />
    protected override string Said(string scope, XmipStatus status)
    {
        return English.Paused(scope, status);
    }
}

/// <summary>
/// <para type="synopsis">Resume everything at and beneath a scope.</para>
/// </summary>
[Cmdlet(VerbsLifecycle.Resume, "XmipScope", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class ResumeXmipScopeCommand : XmipScopeCommand
{
    /// <inheritdoc />
    protected override ScopeAction Action => ScopeAction.Resume;

    /// <inheritdoc />
    protected override XmipStatus Apply(Operator runtime, string scope)
    {
        return runtime.ResumeScope(scope);
    }

    /// <inheritdoc />
    protected override string Said(string scope, XmipStatus status)
    {
        return English.Resumed(scope, status);
    }
}
