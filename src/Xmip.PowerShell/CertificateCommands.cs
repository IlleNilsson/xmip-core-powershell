using System.Management.Automation;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">Makes a certificate the cluster's authority stands
/// behind: a person's key and request or its signing; a node's key and
/// certificate, or its request, signing and import across an air gap; or
/// the authority itself.</para>
/// </summary>
/// <remarks>
/// An operator's certificate (ADR-0034, amendment 2026-10-15): client
/// authentication alone, its subject the person — <c>CN=&lt;person&gt;</c>.
/// <c>-Operator</c> makes a fresh key and a request naming the person into
/// <c>-Path</c>, as <c>operator.key</c> and <c>operator.csr</c>;
/// <c>-Request</c> with <c>-Configuration</c> signs one by the authority
/// that node configuration's <c>[provision.operator]</c> names.
/// A node's, for server and client authentication, ninety days:
/// <c>-Node</c> with <c>-Cluster</c> and <c>-Authority</c> issues it a fresh
/// key and certificate from the authority kept in that folder, made there
/// where none is (ADR-0015, amendment 2026-10-21). Across an air gap
/// (ADR-0034, amendment 2026-10-22): <c>-Node</c> alone, on the node, writes
/// <c>node.next.key</c>, which stays, and <c>node.csr</c>, to carry;
/// <c>-Request</c> with <c>-Authority</c> signs a carried request by the
/// authority kept there, never made, its names exactly <c>-Name</c> where
/// given; <c>-Import</c>, on the node, checks the carried chain and writes
/// <c>node.pem</c>, <c>node.key</c> and <c>anchors.pem</c>. The authority:
/// <c>-Authority</c> with <c>-Cluster</c> sets it up, <c>-Publish</c> the
/// plain-HTTP address its revocation list and certificate are fetched from.
/// Running a signing is the approval, out of band. One cmdlet makes, and
/// <c>Get-XmipCertificate</c> reads and acts; the files and the rules are
/// the runtime's, reached through <see cref="OperatorCertificate"/>,
/// <see cref="NodeCertificate"/> and <see cref="AuthorityCertificate"/>,
/// the ones <c>xmip certificate</c> uses.
/// </remarks>
[Cmdlet(VerbsCommon.New, "XmipCertificate", SupportsShouldProcess = true,
    DefaultParameterSetName = Making)]
[OutputType(typeof(OperatorRequested), ParameterSetName = [Making])]
[OutputType(typeof(OperatorSigned), ParameterSetName = [Signing])]
[OutputType(typeof(NodeCertified), ParameterSetName = [Issuing])]
[OutputType(typeof(CarriedRequest), ParameterSetName = [Requesting])]
[OutputType(typeof(CarriedSigned), ParameterSetName = [CarriedSigning])]
[OutputType(typeof(CarriedImport), ParameterSetName = [Importing])]
[OutputType(typeof(AuthorityState), ParameterSetName = [SettingUp])]
public sealed class NewXmipCertificateCommand : CertificateCommand
{
    private const string Making = "Operator";
    private const string Signing = "Request";
    private const string Issuing = "Node";
    private const string Requesting = "NodeRequest";
    private const string CarriedSigning = "NodeSigning";
    private const string Importing = "Import";
    private const string SettingUp = "Authority";

    /// <summary>
    /// <para type="description">The person a key and request are made for,
    /// as their directory knows them: alice@example.org.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Making)]
    public string Operator { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The request to sign, a CERTIFICATE REQUEST
    /// PEM file: a person's, what -Operator wrote, with -Configuration; a
    /// node's carried node.csr with -Authority.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Signing,
        ValueFromPipelineByPropertyName = true)]
    [Parameter(Mandatory = true, ParameterSetName = CarriedSigning,
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
    /// to, or whose request is made, as its cluster's xmip.toml declares
    /// it: R1.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Issuing)]
    [Parameter(Mandatory = true, ParameterSetName = Requesting)]
    public string Node { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The cluster: with -Node and -Authority, the
    /// cluster the node is of; with -Authority alone, the cluster whose
    /// authority is set up. It names the authority where one is made.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Issuing)]
    [Parameter(Mandatory = true, ParameterSetName = SettingUp)]
    public string Cluster { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The folder the cluster's authority is kept
    /// in, authority.key, authority.pem and its register; made there where
    /// none is, except to sign a carried request.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Issuing)]
    [Parameter(Mandatory = true, ParameterSetName = CarriedSigning)]
    [Parameter(Mandatory = true, ParameterSetName = SettingUp)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Every name the node is reached by besides
    /// its own: R1.xmip.test, C1, C1.xmip.test. To sign a carried request,
    /// the names it must ask for, its node's own among them.</para>
    /// </summary>
    [Parameter(ParameterSetName = Issuing)]
    [Parameter(ParameterSetName = Requesting)]
    [Parameter(ParameterSetName = CarriedSigning)]
    public string[] Name { get; set; } = [];

    /// <summary>
    /// <para type="description">The chain the authority signed for a
    /// carried request, to import into the node's -Path, the folder the
    /// request was made in.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Importing)]
    public string Import { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">With -Authority and -Cluster, the plain-HTTP
    /// address the authority publishes its revocation list (/crl) and
    /// certificate (/authority.cer) at, http://ca.xmip.test:8080, which
    /// every certificate it signs from then names.</para>
    /// </summary>
    [Parameter(ParameterSetName = SettingUp)]
    public string? Publish { get; set; }

    /// <summary>
    /// <para type="description">With -Operator, the folder the key and
    /// request are written into; with -Request, the file the chain is
    /// written to, nothing overwritten; with -Node, the folder node.pem,
    /// node.key and anchors.pem are written into, or a node's request; with
    /// -Import, the node's folder its request was made in. The current
    /// folder unless given.</para>
    /// </summary>
    [Parameter]
    public string? Path { get; set; }

    /// <inheritdoc />
    protected override void Process()
    {
        string? path = Path is null ? null : GetUnresolvedProviderPathFromPSPath(Path);
        string here = path ?? SessionState.Path.CurrentFileSystemLocation.ProviderPath;
        string Full(string given) => GetUnresolvedProviderPathFromPSPath(given);

        switch (ParameterSetName)
        {
            case Issuing:
                Act(here, $"Issue node {Node} its key and certificate", () =>
                {
                    NodeCertified issued = NodeCertificate.Issue(
                        Certificates, Full(Authority), Cluster, Node, Name, here);
                    return (issued.Ok, issued.Refused, issued, issued.Certificate);
                });
                return;
            case Requesting:
                Act(here, $"Make node {Node}'s request to carry to the authority", () =>
                {
                    CarriedRequest asked = AuthorityCertificate.Request(
                        Certificates, Node, Name, here);
                    return (asked.Ok, asked.Refused, asked, asked.Request);
                });
                return;
            case CarriedSigning:
                Act(Full(Request), "Sign by the cluster's authority", () =>
                {
                    CarriedSigned signed = AuthorityCertificate.Sign(
                        Certificates, Full(Authority), Full(Request), Name, path);
                    return (signed.Ok, signed.Refused, signed, signed.Serial);
                });
                return;
            case Importing:
                Act(here, $"Import {Import} as this node's certificate", () =>
                {
                    CarriedImport imported = AuthorityCertificate.Import(
                        Certificates, Full(Import), here);
                    return (imported.Ok, imported.Refused, imported, imported.Serial);
                });
                return;
            case SettingUp:
                Act(Full(Authority), $"Set up the authority of cluster {Cluster}", () =>
                {
                    AuthorityState set = AuthorityCertificate.SetUp(
                        Certificates, Full(Authority), Cluster, Publish);
                    return (set.Ok, set.Refused, set, set.Fingerprint);
                });
                return;
            case Making:
                if (ShouldProcess(here, $"Make a key and certificate request for {Operator}"))
                {
                    OperatorRequested made =
                        OperatorCertificate.Request(Certificates, Operator, here);
                    Answer(made.Ok, made.Refused, made, here);
                }

                return;
            default:
                Act(Full(Request), "Sign by the cluster's authority", () =>
                {
                    OperatorSigned signed = OperatorCertificate.Sign(
                        Certificates, Full(Request), Full(Configuration), path);
                    return (signed.Ok, signed.Refused, signed, signed.Written ?? "signed");
                });
                return;
        }
    }
}
