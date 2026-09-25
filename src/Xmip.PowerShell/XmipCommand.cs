using System.Diagnostics.CodeAnalysis;
using System.Management.Automation;
using Xmip.Abi.Operate;

namespace Xmip.PowerShell;

/// <summary>
/// What every cmdlet in this module shares: its audit (ADR-0062). Every
/// error a cmdlet writes (<see cref="Refuse"/>) or ends on
/// (<see cref="Stop"/>), and every exception that leaves it, is recorded as a
/// failure of the cmdlet — the action its name, the bound parameters its
/// properties — before PowerShell shows it; an act that changes the estate
/// records its beginning and end (<see cref="Acting"/>, <see cref="Acted"/>).
/// A cmdlet writes its steps in <see cref="Begin"/>, <see cref="Process"/>
/// and <see cref="End"/>, which is what lets the audit be written here once.
/// </summary>
public abstract class XmipCommand : PSCmdlet
{
    // Set when a failure was recorded on its way out, so the exception
    // PowerShell raises for it is not recorded a second time.
    private bool _recorded;

    /// <summary>The action this cmdlet's records carry: its name.</summary>
    protected string AuditAction => MyInvocation?.MyCommand?.Name ?? GetType().Name;

    /// <inheritdoc />
    protected sealed override void BeginProcessing()
    {
        Audited(Begin);
    }

    /// <inheritdoc />
    protected sealed override void ProcessRecord()
    {
        Audited(Process);
    }

    /// <inheritdoc />
    protected sealed override void EndProcessing()
    {
        Audited(End);
    }

    /// <summary>What <c>BeginProcessing</c> does.</summary>
    protected virtual void Begin()
    {
    }

    /// <summary>What <c>ProcessRecord</c> does.</summary>
    protected virtual void Process()
    {
    }

    /// <summary>What <c>EndProcessing</c> does.</summary>
    protected virtual void End()
    {
    }

    /// <summary>A non-terminating error: recorded, then written.</summary>
    protected void Refuse(ErrorRecord error)
    {
        ModuleAudit.Failed(AuditAction, error, ModuleAudit.Properties(MyInvocation));
        WriteError(error);
    }

    /// <summary>A terminating error: recorded, then thrown.</summary>
    [DoesNotReturn]
    protected void Stop(ErrorRecord error)
    {
        ModuleAudit.Failed(AuditAction, error, ModuleAudit.Properties(MyInvocation));
        _recorded = true;
        ThrowTerminatingError(error);

        throw new InvalidOperationException("ThrowTerminatingError returned.");
    }

    /// <summary>Record that an act on <paramref name="target"/> begins.</summary>
    protected void Acting(string target)
    {
        Dictionary<string, string> said = ModuleAudit.Properties(MyInvocation);
        said["target"] = target;
        ModuleAudit.Open().Record(
            AuditAction, AuditPhase.Begin, AuditSeverity.Information, properties: said);
    }

    /// <summary>Record that the act on <paramref name="target"/> finished,
    /// saying <paramref name="result"/>. An act that did not is a
    /// <see cref="Refuse"/>.</summary>
    protected void Acted(string target, string result)
    {
        Dictionary<string, string> said = ModuleAudit.Properties(MyInvocation);
        said["target"] = target;
        ModuleAudit.Open().Record(
            AuditAction, AuditPhase.Finished, AuditSeverity.Information, result, said);
    }

    // The exception leaves untouched: the filter records it and declines,
    // so PowerShell reports it exactly as it would have.
    private void Audited(Action step)
    {
        try
        {
            step();
        }
        catch (Exception failure) when (Unrecorded(failure))
        {
            throw;
        }
    }

    private bool Unrecorded(Exception failure)
    {
        if (!_recorded && failure is not PipelineStoppedException)
        {
            _recorded = true;
            ModuleAudit.Open().Failed(
                AuditAction, failure, ModuleAudit.Properties(MyInvocation));
        }

        return false;
    }
}
