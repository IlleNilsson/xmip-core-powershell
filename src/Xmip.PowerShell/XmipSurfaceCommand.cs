using System.Management.Automation;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// What every cmdlet that reads a scope shares: the surface it reads and the
/// scopes an operator named. The surface is the one <c>xmip-cli</c> would
/// read — <c>-Remote</c>, then <c>-Snapshot</c>, then <c>-Library</c>, then
/// <c>xmip.powershell.toml</c> beside the module, then the runtime rule
/// (<see cref="SurfaceChoice.Stated"/>, ADR-0052 clause 1) — and a scope may
/// be a wildcard over the scopes that exist, selected by the one
/// <see cref="ScopeSelection"/> the executable uses (ADR-0059 clause 7).
/// Until 2026-09-24 every cmdlet demanded a library path, loaded the binding
/// itself and took a scope literally; the executable did none of that.
/// </summary>
public abstract class XmipSurfaceCommand : XmipCommand
{
    private IOperatorSurface? _surface;

    // Cancelled when the pipeline stops; how many follows are reading.
    private readonly CancellationTokenSource _stopping = new();
    private int _following;

    /// <summary>
    /// <para type="description">The runtime's native library to load, the one
    /// exporting xmip_operate_v1, over what the module's document says.
    /// Omitted, the document decides, and with no surface named there the
    /// library is found by the one rule: RuntimeLibrary in the document, else
    /// XMIP_RUNTIME_LIBRARY, else beside the module.</para>
    /// </summary>
    [Parameter]
    public string? Library { get; set; }

    /// <summary>
    /// <para type="description">One published snapshot to read — one
    /// cluster's — over the library and the document.</para>
    /// </summary>
    [Parameter]
    public string? Snapshot { get; set; }

    /// <summary>
    /// <para type="description">A web host on another machine to follow, such
    /// as https://host:5443, over everything else: TLS, presenting and trusting
    /// what xmip.powershell.toml or XMIP_CERTIFICATE, XMIP_PRIVATE_KEY and
    /// XMIP_TRUST_ANCHOR name; plain http only to this machine (ADR-0063
    /// clause 1).</para>
    /// </summary>
    [Parameter]
    public string? Remote { get; set; }

    /// <summary>The surface, open and answering from BeginProcessing on.</summary>
    protected IOperatorSurface Surface =>
        _surface ?? throw new InvalidOperationException("The surface is not open yet.");

    /// <summary>
    /// What one argument selects on the surface, or null after writing the
    /// refusal when a pattern names nothing — a non-terminating error, so the
    /// next argument is still answered.
    /// </summary>
    protected ScopeSelection? Select(string argument)
    {
        ScopeSelection? chosen = ScopeSelection.Of(Surface, argument, out string refusal);

        if (chosen is null)
        {
            Refuse(new ErrorRecord(
                new ItemNotFoundException(refusal),
                "XmipScopePatternUnmatched",
                ErrorCategory.ObjectNotFound,
                argument));
        }

        return chosen;
    }

    /// <inheritdoc />
    protected override void Begin()
    {
        SurfaceLine line = new(Remote, Resolved(Snapshot), Resolved(Library));

        if (line.Refusal is { } refusal)
        {
            Stop(new ErrorRecord(
                new ArgumentException(refusal),
                "XmipRemoteNotAWebHost",
                ErrorCategory.InvalidArgument,
                Remote));
        }

        _surface = ModuleSurface.Answering(line, out string reason);

        if (_surface is null)
        {
            Stop(new ErrorRecord(
                new InvalidOperationException(reason),
                "XmipSurfaceUnavailable",
                ErrorCategory.ResourceUnavailable,
                line));
        }
    }

    /// <inheritdoc />
    protected override void End()
    {
        Release();
    }

    /// <summary>
    /// <c>-Follow</c>: the rows <paramref name="read"/> answers for what
    /// <paramref name="chosen"/> names, now and again each time the
    /// publication advances and the answer changed, until Ctrl+C — the one
    /// follow <c>xmip-cli --follow</c> uses (<see cref="SurfaceFollow"/>), so
    /// a wildcard is matched again at every notice. Ctrl+C is its normal end.
    /// </summary>
    protected void Following<T>(
        ScopeSelection chosen, Func<IOperatorSurface, ScopeSelection, IReadOnlyList<T>> read)
    {
        Interlocked.Increment(ref _following);
        IAsyncEnumerator<IReadOnlyList<T>> answers = SurfaceFollow
            .Changes(Surface, chosen, read, SurfaceFollow.Rows<T>(), _stopping.Token)
            .GetAsyncEnumerator(_stopping.Token);

        try
        {
            while (answers.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                foreach (T row in answers.Current)
                {
                    WriteObject(row);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C: the pipeline is stopping, and the follow with it.
        }
        finally
        {
            answers.DisposeAsync().AsTask().GetAwaiter().GetResult();
            Interlocked.Decrement(ref _following);

            if (_stopping.IsCancellationRequested)
            {
                Release();
            }
        }
    }

    /// <inheritdoc />
    protected override void StopProcessing()
    {
        _stopping.Cancel();

        // A follow in flight still reads the surface; it releases it as it
        // ends, never under its feet.
        if (Volatile.Read(ref _following) == 0)
        {
            Release();
        }
    }

    // A path as PowerShell reads one, from the current location and through
    // its providers; nothing stated stays nothing.
    private string? Resolved(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? null : GetUnresolvedProviderPathFromPSPath(path);
    }

    private void Release()
    {
        (_surface as IDisposable)?.Dispose();
        _surface = null;
    }
}
