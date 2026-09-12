using System.Management.Automation;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>Shared implementation for the five scope lifecycle commands.</summary>
public abstract class XmipScopeCommand : PSCmdlet
{
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true)]
    public string[] Path { get; set; } = [];

    [Parameter]
    public string Who { get; set; } = Environment.UserName;

    protected abstract string Action { get; }

    protected override void ProcessRecord()
    {
        foreach (string path in Path)
        {
            string scope = XmipProvider.ToScope(path);

            if (!ShouldProcess(scope, Action))
            {
                continue;
            }

            ScopeOperation operation = PromptMonitor.Control(scope, Action, Who);

            if (!operation.Applied)
            {
                WriteError(new ErrorRecord(
                    new InvalidOperationException(operation.Result),
                    "XmipScopeOperationRefused", ErrorCategory.InvalidOperation, scope));
                continue;
            }

            WriteObject(operation);
        }
    }
}

[Cmdlet(VerbsLifecycle.Suspend, "XmipScope", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class SuspendXmipScopeCommand : XmipScopeCommand
{
    protected override string Action => "Pause";
}

[Cmdlet(VerbsLifecycle.Resume, "XmipScope", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class ResumeXmipScopeCommand : XmipScopeCommand
{
    protected override string Action => "Resume";
}

[Cmdlet(VerbsLifecycle.Start, "XmipScope", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class StartXmipScopeCommand : XmipScopeCommand
{
    protected override string Action => "Start";
}

[Cmdlet(VerbsLifecycle.Stop, "XmipScope", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class StopXmipScopeCommand : XmipScopeCommand
{
    protected override string Action => "Stop";
}

[Cmdlet(VerbsLifecycle.Restart, "XmipScope", SupportsShouldProcess = true)]
[OutputType(typeof(ScopeOperation))]
public sealed class RestartXmipScopeCommand : XmipScopeCommand
{
    protected override string Action => "Restart";
}
