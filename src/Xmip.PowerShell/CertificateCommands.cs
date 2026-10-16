using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">Makes a person's key and certificate request, or
/// signs one by the cluster's authority.</para>
/// </summary>
/// <remarks>
/// An operator's certificate (ADR-0034, amendment 2026-10-15): client
/// authentication alone, its subject the person — <c>CN=&lt;person&gt;</c>,
/// the caller a node's operate listener proves and grants its
/// <c>[operate]</c> role. <c>-Operator</c> makes a fresh key and a request
/// naming the person into <c>-Path</c>, as <c>operator.key</c> and
/// <c>operator.csr</c>; <c>-Request</c> with <c>-Configuration</c> signs a
/// request by the authority that node configuration's
/// <c>[provision.operator]</c> names, the chain written to <c>-Path</c> where
/// one is given. Running the signing is the approval, out of band. One
/// cmdlet, two acts on one noun; the key's and the request's files, private
/// to the user the session runs as, and the rules are the runtime's, reached
/// through <see cref="OperatorCertificate"/>, the ones
/// <c>xmip-cli certificate</c> uses.
/// </remarks>
[Cmdlet(VerbsCommon.New, "XmipCertificate", SupportsShouldProcess = true,
    DefaultParameterSetName = Making)]
[OutputType(typeof(OperatorRequested), ParameterSetName = [Making])]
[OutputType(typeof(OperatorSigned), ParameterSetName = [Signing])]
public sealed class NewXmipCertificateCommand : XmipCommand
{
    private const string Making = "Operator";
    private const string Signing = "Request";

    private RuntimeCertificates? _certificates;

    /// <summary>
    /// <para type="description">The person a key and request are made for,
    /// as their directory knows them: alice@example.org.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Making)]
    public string Operator { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The request to sign, a CERTIFICATE REQUEST
    /// PEM file; what -Operator wrote.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Signing,
        ValueFromPipelineByPropertyName = true)]
    public string Request { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The node configuration whose
    /// [provision.operator] names the authority that signs and how long its
    /// certificate is valid.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Signing)]
    public string Configuration { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">With -Operator, the folder the key and
    /// request are written into, the current one unless given; with
    /// -Request, the file the chain is written to. Nothing is
    /// overwritten.</para>
    /// </summary>
    [Parameter]
    public string? Path { get; set; }

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
            _certificates = RuntimeLibrary.Rules.Certificates;
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
        string? path = Path is null ? null : GetUnresolvedProviderPathFromPSPath(Path);

        if (ParameterSetName == Making)
        {
            string folder = path ?? SessionState.Path.CurrentFileSystemLocation.ProviderPath;

            if (ShouldProcess(folder, $"Make a key and certificate request for {Operator}"))
            {
                OperatorRequested made =
                    OperatorCertificate.Request(_certificates!, Operator, folder);
                Answer(made.Ok, made.Refused, made, folder);
            }

            return;
        }

        string request = GetUnresolvedProviderPathFromPSPath(Request);

        if (ShouldProcess(request, "Sign by the cluster's authority"))
        {
            Acting(request);

            try
            {
                OperatorSigned signed = OperatorCertificate.Sign(
                    _certificates!, request, GetUnresolvedProviderPathFromPSPath(Configuration),
                    path);
                Answer(signed.Ok, signed.Refused, signed, request);

                if (signed.Ok)
                {
                    Acted(request, signed.Written ?? "signed");
                }
            }
            catch (InvalidOperationException remote)
            {
                Refuse(new ErrorRecord(
                    remote, "XmipCertificateRefused", ErrorCategory.InvalidOperation, request));
            }
        }
    }

    // The object where it was done; the refusal in words where not.
    private void Answer(bool ok, string refused, object answer, string target)
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
