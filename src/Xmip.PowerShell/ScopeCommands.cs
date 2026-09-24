using System.Management.Automation;
using Xmip.Surface;

namespace Xmip.PowerShell;

// The two acts the operator boundary carries (ADR-0027 clause 5), as cmdlets.
// Suspend rather than Pause because Pause is not an approved verb. Each emits
// the ScopeOperation the surface returns and `xmip-cli pause` renders, so a
// pipeline object and an exit code say the same thing, decided in one place
// (IOperatorSurface.Control). There is no Start, Stop or Restart: the
// boundary has no such call, and the thing that watches must not be able to
// stop the thing it watches.

/// <summary>What the two scope cmdlets share: the surface, the scopes, the act.</summary>
public abstract class XmipScopeCommand : XmipSurfaceCommand
{
    /// <summary>
    /// <para type="description">The Xmip URI to act on, such as
    /// xmip:///edge-01, or a wildcard over the scopes that exist. Everything
    /// beneath each is included; a wildcard names several subtrees and opens
    /// no wider one.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    [SupportsWildcards]
    public string[] Scope { get; set; } = [];

    /// <summary>The act, and how ShouldProcess names it.</summary>
    protected abstract ScopeAction Action { get; }

    /// <summary>Who is acting, as the runtime records it.</summary>
    protected virtual string Actor => Environment.UserName;

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        foreach (string argument in Scope)
        {
            if (Select(argument) is not { } chosen)
            {
                continue;
            }

            foreach (string scope in chosen.Scopes)
            {
                if (!ShouldProcess(scope, Action.ToString()))
                {
                    continue;
                }

                ScopeOperation operation = Surface.Control(scope, Action, Actor);

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
    protected override string Actor => Who;
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
}
