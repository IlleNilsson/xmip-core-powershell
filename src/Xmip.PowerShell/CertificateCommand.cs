using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// What <c>New-XmipCertificate</c> and <c>Get-XmipCertificate</c> share: the
/// runtime's certificate exports, loaded once a call (<c>xmip_operate.h</c>
/// sections 19, 22 and 23), and one act — asked with -WhatIf and -Confirm,
/// audited as it begins and as it ends, its record written where it was
/// done and its refusal in the runtime's words where it was not.
/// </summary>
public abstract class CertificateCommand : XmipCommand
{
    private RuntimeCertificates? _certificates;

    /// <summary>
    /// <para type="description">The runtime's native library to ask.
    /// Omitted, it is found by the one rule: RuntimeLibrary in the module's
    /// document, else XMIP_RUNTIME_LIBRARY, else beside the module.</para>
    /// </summary>
    [Parameter]
    public string? Library { get; set; }

    /// <summary>The runtime's certificate exports.</summary>
    protected RuntimeCertificates Certificates =>
        _certificates ?? throw new InvalidOperationException("The runtime is not loaded.");

    /// <inheritdoc />
    protected override void Begin()
    {
        string? library = string.IsNullOrWhiteSpace(Library)
            ? null
            : GetUnresolvedProviderPathFromPSPath(Library);
        _ = ModuleSurface.Runtime(library);

        try
        {
            _certificates = RuntimeLibrary.Rules.Certificates;
        }
        catch (InvalidOperationException unloaded)
        {
            Stop(new ErrorRecord(
                unloaded, "XmipRuntimeUnloadable", ErrorCategory.ResourceUnavailable, Library));
        }
    }

    /// <summary>
    /// The act <paramref name="action"/> on <paramref name="target"/>, where
    /// the person agrees: audited as it begins, <paramref name="act"/> asked,
    /// its record written and audited as it ends where it was done, its
    /// refusal written where it was not.
    /// </summary>
    protected void Act(
        string target, string action,
        Func<(bool Ok, string Refused, object Answer, string Result)> act)
    {
        ArgumentNullException.ThrowIfNull(act);

        if (!ShouldProcess(target, action))
        {
            return;
        }

        Acting(target);

        try
        {
            (bool ok, string refused, object answer, string result) = act();
            Answer(ok, refused, answer, target);

            if (ok)
            {
                Acted(target, result);
            }
        }
        catch (InvalidOperationException remote)
        {
            Refuse(new ErrorRecord(
                remote, "XmipCertificateRefused", ErrorCategory.InvalidOperation, target));
        }
    }

    /// <summary>The record where it was done; the refusal in words where
    /// not.</summary>
    protected void Answer(bool ok, string refused, object answer, string target)
    {
        if (ok)
        {
            WriteObject(answer);
            return;
        }

        Refuse(new ErrorRecord(
            new InvalidOperationException(refused),
            "XmipCertificateRefused",
            ErrorCategory.InvalidOperation,
            target));
    }
}
