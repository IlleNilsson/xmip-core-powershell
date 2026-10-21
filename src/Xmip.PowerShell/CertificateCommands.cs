using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">Makes a person's key and certificate request, signs
/// one by the cluster's authority, or issues a node its key and certificate
/// from that authority.</para>
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
/// one is given. Running the signing is the approval, out of band.
/// <c>-Node</c> with <c>-Cluster</c> and <c>-Authority</c> issues a node put
/// on a machine of its own a fresh key and a certificate for server and
/// client authentication from the cluster's authority kept in that folder,
/// made there where none is, for the node's own name and every
/// <c>-Name</c>, into <c>-Path</c> as <c>node.pem</c>, <c>node.key</c> and
/// <c>anchors.pem</c>, the files a node's <c>[operate]</c> names (ADR-0015,
/// amendment 2026-10-21). One cmdlet, three acts on one noun; the files,
/// private to the user the session runs as, and the rules are the
/// runtime's, reached through <see cref="OperatorCertificate"/> and
/// <see cref="NodeCertificate"/>, the ones <c>xmip-cli certificate</c> uses.
/// </remarks>
[Cmdlet(VerbsCommon.New, "XmipCertificate", SupportsShouldProcess = true,
    DefaultParameterSetName = Making)]
[OutputType(typeof(OperatorRequested), ParameterSetName = [Making])]
[OutputType(typeof(OperatorSigned), ParameterSetName = [Signing])]
[OutputType(typeof(NodeCertified), ParameterSetName = [Issuing])]
public sealed class NewXmipCertificateCommand : XmipCommand
{
    private const string Making = "Operator";
    private const string Signing = "Request";
    private const string Issuing = "Node";

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
    /// <para type="description">The node a key and certificate are issued
    /// to, as its cluster's xmip.toml declares it: R1.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Issuing)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">With -Node, the cluster it is a node of,
    /// which names the authority where one is made.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Issuing)]
    public string Cluster { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">With -Node, the folder the cluster's
    /// authority is kept in, authority.key and authority.pem; made there
    /// where none is.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Issuing)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">With -Node, every name the node is reached
    /// by besides its own: R1.xmip.test, C1, C1.xmip.test.</para>
    /// </summary>
    [Parameter(ParameterSetName = Issuing)]
    public string[] Name { get; set; } = [];

    /// <summary>
    /// <para type="description">With -Operator, the folder the key and
    /// request are written into, the current one unless given; with
    /// -Request, the file the chain is written to, nothing overwritten; with
    /// -Node, the folder node.pem, node.key and anchors.pem are written into,
    /// in place of what was there, the current one unless given.</para>
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

        if (ParameterSetName == Issuing)
        {
            string into = path ?? SessionState.Path.CurrentFileSystemLocation.ProviderPath;
            string authority = GetUnresolvedProviderPathFromPSPath(Authority);

            if (ShouldProcess(into, $"Issue node {Node} its key and certificate"))
            {
                Acting(into);

                try
                {
                    NodeCertified issued = NodeCertificate.Issue(
                        _certificates!, authority, Cluster, Node, Name, into);
                    Answer(issued.Ok, issued.Refused, issued, into);

                    if (issued.Ok)
                    {
                        Acted(into, issued.Certificate);
                    }
                }
                catch (InvalidOperationException remote)
                {
                    Refuse(new ErrorRecord(
                        remote, "XmipCertificateRefused", ErrorCategory.InvalidOperation, into));
                }
            }

            return;
        }

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
