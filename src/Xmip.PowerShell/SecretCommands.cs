using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Security;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// <para type="synopsis">Set the secret a Location presents to the party it
/// connects to, or list the secrets kept.</para>
/// </summary>
/// <remarks>
/// A Location that connects to a party names its proof with
/// <c>secret = "&lt;name&gt;"</c>; the secret itself is never in the TOML. It
/// is kept in the Xmip Administration Database, in a table of its own,
/// sealed at rest on an embedded Storage node, and read as the Location next
/// connects — no restart (ADR-0019, amendment 2026-10-29). Its kinds:
/// password, key (an SSH private key, OpenSSH's form, unencrypted) and
/// certificate (a PEM chain, leaf first, with its private key). One cmdlet
/// for the noun, the act a parameter on it (ADR-0014, amendment
/// 2026-09-15): setting by default, -List to list. The value crosses to the
/// node inside the surface's connection — the local endpoint or Xmip's
/// mutual TLS — and is never read back out: a listing says each secret's
/// name, kind, and when and by whom it was set. Setting, with -WhatIf and
/// -Confirm: <see cref="SecretOperation"/>. Listing:
/// <see cref="SecretRecord"/>.
/// </remarks>
[Cmdlet(
    VerbsCommon.Set, "XmipSecret",
    DefaultParameterSetName = ValueSet,
    SupportsShouldProcess = true)]
[OutputType(typeof(SecretOperation), ParameterSetName = [ValueSet, PathSet])]
[OutputType(typeof(SecretRecord), ParameterSetName = [ListSet])]
public sealed class SetXmipSecretCommand : XmipSurfaceCommand
{
    private const string ValueSet = "Value";
    private const string PathSet = "Path";
    private const string ListSet = "List";

    /// <summary>
    /// <para type="description">The secret's name, as a Location's secret
    /// names it: letters, digits, '.', '_' and '-'.</para>
    /// </summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ValueSet)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PathSet)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">What it is: password, key or
    /// certificate.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ValueSet)]
    [Parameter(Mandatory = true, ParameterSetName = PathSet)]
    [ArgumentCompleter(typeof(SecretKinds))]
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// <para type="description">The value, as a secure string: asked for
    /// without echo where it is not given.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ValueSet)]
    public SecureString? Value { get; set; }

    /// <summary>
    /// <para type="description">A file holding the value — a key's or a
    /// certificate's PEM — read once and not kept.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = PathSet)]
    public string? Path { get; set; }

    /// <summary>
    /// <para type="description">List the secrets kept, never a
    /// value.</para>
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ListSet)]
    public SwitchParameter List { get; set; }

    /// <summary>
    /// <para type="description">The node it is set through, such as
    /// xmip:///&lt;cluster&gt;/node/&lt;node&gt;. Omitted, the one node the
    /// surface reaches.</para>
    /// </summary>
    [Parameter]
    [Alias("Node")]
    public string? Location { get; set; }

    /// <summary>
    /// <para type="description">Who acts, as the node's audit records it. The
    /// current user when omitted.</para>
    /// </summary>
    [Parameter(ParameterSetName = ValueSet)]
    [Parameter(ParameterSetName = PathSet)]
    public string? Who { get; set; }

    /// <inheritdoc />
    protected override void Process()
    {
        string node = Location ?? string.Empty;

        if (ParameterSetName == ListSet)
        {
            Listed(node);
            return;
        }

        string target = $"the secret '{Name}'";

        if (!ShouldProcess(target, SecretOperation.Act))
        {
            return;
        }

        string? value = Read();

        if (value is null)
        {
            return;
        }

        Acting(target);
        SecretOperation done = Surface.SetSecret(node, Name, Kind, value, ScopeOperation.Who(Who));

        if (!done.Applied)
        {
            Refuse(new ErrorRecord(
                new InvalidOperationException(done.Result),
                "XmipSecretRefused",
                ErrorCategory.InvalidOperation,
                target));

            return;
        }

        Acted(target, done.Result);
        WriteObject(done);
    }

    private void Listed(string node)
    {
        IReadOnlyList<SecretRecord> secrets;

        try
        {
            secrets = Surface.Secrets(node);
        }
        catch (InvalidOperationException refused)
        {
            Refuse(new ErrorRecord(
                refused, "XmipSecretsUnlisted", ErrorCategory.ResourceUnavailable, node));

            return;
        }

        foreach (SecretRecord secret in secrets)
        {
            WriteObject(secret);
        }
    }

    // The value as text, from the secure string or the file; null where the
    // file could not be read, said as an error.
    private string? Read()
    {
        if (ParameterSetName == PathSet)
        {
            try
            {
                return File.ReadAllText(GetUnresolvedProviderPathFromPSPath(Path));
            }
            catch (IOException failed)
            {
                Refuse(new ErrorRecord(
                    failed, "XmipSecretUnread", ErrorCategory.ReadError, Path));

                return null;
            }
        }

        nint text = Marshal.SecureStringToGlobalAllocUnicode(Value!);

        try
        {
            return Marshal.PtrToStringUni(text) ?? string.Empty;
        }
        finally
        {
            Marshal.ZeroFreeGlobalAllocUnicode(text);
        }
    }
}

/// <summary>What Tab offers for <c>-Kind</c>: the kinds a secret is, as
/// <see cref="SecretRecord.Kinds"/> says them, so no word list is kept
/// here.</summary>
public sealed class SecretKinds : IArgumentCompleter
{
    /// <inheritdoc />
    public IEnumerable<CompletionResult> CompleteArgument(
        string commandName,
        string parameterName,
        string wordToComplete,
        System.Management.Automation.Language.CommandAst commandAst,
        System.Collections.IDictionary fakeBoundParameters)
    {
        string typed = (wordToComplete ?? string.Empty).Trim('\'', '"');

        return SecretRecord.Kinds
            .Where(kind => kind.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
            .Select(kind => new CompletionResult(
                kind, kind, CompletionResultType.ParameterValue, kind));
    }
}
