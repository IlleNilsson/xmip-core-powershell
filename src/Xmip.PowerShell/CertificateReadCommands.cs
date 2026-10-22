using System.Management.Automation;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">Reads the cluster's authority and every certificate
/// it signed, or a certificate file and whether its renewal is due; and
/// revokes, exports or backs up from the authority.</para>
/// </summary>
/// <remarks>
/// The cluster's own authority, the premises' own ACME service (ADR-0034,
/// amendment 2026-10-22). <c>-Authority</c> alone reads the authority kept
/// in that folder: its certificate's SHA-256, the address it publishes at,
/// its revocation list and every certificate it signed, newest first, each
/// with its standing — current, renewal due, expired or revoked.
/// <c>-Path</c> reads a certificate file's leaf and its standing: on an
/// air-gapped node, renewal due says to make the next request
/// (<c>New-XmipCertificate -Node</c>). One cmdlet reads, the act a
/// parameter on it (ADR-0014, amendment 2026-09-15), as
/// <c>Get-XmipSubscription</c> pauses: <c>-Revoke</c> revokes the
/// certificate <c>-Serial</c> for <c>-Reason</c>, listed at once, a
/// certificate read by an earlier call piping in by its Serial;
/// <c>-Export</c> writes the authority's certificate, <c>Pem</c> for a
/// Linux trust store or <c>Der</c> for Windows, or its revocation list,
/// <c>Crl</c>, never over a file; <c>-Backup</c> copies the whole authority,
/// its key among it, into an empty folder. Each act is audited, with
/// -WhatIf and -Confirm. The rules are the runtime's, through
/// <see cref="AuthorityCertificate"/>, the one <c>xmip certificate</c> uses.
/// </remarks>
[Cmdlet(VerbsCommon.Get, "XmipCertificate", SupportsShouldProcess = true,
    DefaultParameterSetName = Reading)]
[OutputType(typeof(AuthorityState), ParameterSetName = [Reading])]
[OutputType(typeof(CertificateStanding), ParameterSetName = [File])]
[OutputType(typeof(AuthorityRecord), ParameterSetName = [Revoking])]
[OutputType(typeof(AuthorityExport), ParameterSetName = [Exporting, BackingUp])]
public sealed class GetXmipCertificateCommand : CertificateCommand
{
    private const string Reading = "Authority";
    private const string File = "Path";
    private const string Revoking = "Revoke";
    private const string Exporting = "Export";
    private const string BackingUp = "Backup";

    /// <summary>
    /// <para type="description">The folder the cluster's authority is kept
    /// in.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = Reading)]
    [Parameter(Mandatory = true, ParameterSetName = Revoking)]
    [Parameter(Mandatory = true, ParameterSetName = Exporting)]
    [Parameter(Mandatory = true, ParameterSetName = BackingUp)]
    public string Authority { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">A certificate file, PEM, its leaf first:
    /// node.pem.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = File)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">With -Revoke, the serial of the certificate
    /// the authority signed, hexadecimal, as it lists it.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Revoking,
        ValueFromPipelineByPropertyName = true)]
    public string Serial { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">Revoke the certificate -Serial: the
    /// authority's list names it from now.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Revoking)]
    public SwitchParameter Revoke { get; set; }

    /// <summary>
    /// <para type="description">Why, as RFC 5280 names it.</para>
    /// </summary>
    [Parameter(ParameterSetName = Revoking)]
    [ValidateSet(
        "unspecified", "keyCompromise", "affiliationChanged", "superseded",
        "cessationOfOperation", "privilegeWithdrawn")]
    public string Reason { get; set; } = "unspecified";

    /// <summary>
    /// <para type="description">The file the authority's certificate or its
    /// revocation list is written to, never over a file.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = Exporting)]
    public string Export { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">With -Export: Pem, the default, a .crt a
    /// Linux trust store takes; Der, a .cer Windows takes; Crl, the
    /// revocation list.</para>
    /// </summary>
    [Parameter(ParameterSetName = Exporting)]
    [ValidateSet("Pem", "Der", "Crl")]
    public string Format { get; set; } = "Pem";

    /// <summary>
    /// <para type="description">The empty folder the whole authority is
    /// copied into; the copy is the same authority. Keep it as its key is
    /// kept.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = BackingUp)]
    public string Backup { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override void Process()
    {
        string Full(string given) => GetUnresolvedProviderPathFromPSPath(given);
        string authority = ParameterSetName == File ? string.Empty : Full(Authority);

        switch (ParameterSetName)
        {
            case File:
                CertificateStanding read = AuthorityCertificate.Read(Certificates, Full(Path));
                Answer(read.Ok, read.Refused, read, Full(Path));
                return;
            case Revoking:
                Act(authority, $"Revoke {Serial} for {Reason}", () =>
                {
                    AuthorityRecord revoked = AuthorityCertificate.Revoke(
                        Certificates, authority, Serial, Reason);
                    return (revoked.Ok, revoked.Refused, revoked, revoked.Serial);
                });
                return;
            case Exporting:
                Act(Full(Export), $"Write the authority's {Format.ToUpperInvariant()}", () =>
                {
                    AuthorityExport written = AuthorityCertificate.Export(
                        Certificates, authority, Full(Export), Format.ToLowerInvariant());
                    return (written.Ok, written.Refused, written, written.Written);
                });
                return;
            case BackingUp:
                Act(Full(Backup), "Copy the whole authority, its key among it", () =>
                {
                    AuthorityExport copied = AuthorityCertificate.BackUp(
                        Certificates, authority, Full(Backup));
                    return (copied.Ok, copied.Refused, copied, copied.Written);
                });
                return;
            default:
                AuthorityState state = AuthorityCertificate.Inspect(Certificates, authority);
                Answer(state.Ok, state.Refused, state, authority);
                return;
        }
    }
}
