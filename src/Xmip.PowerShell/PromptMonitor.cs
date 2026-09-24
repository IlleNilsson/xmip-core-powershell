using System.Globalization;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>One colored piece of the prompt segment.</summary>
public sealed record XmipPromptPart(string Text, ConsoleColor Color);

/// <summary>A prompt-safe rendering of the latest operator snapshot: the parts
/// in order, each in its color, and the whole as one line of text.</summary>
public sealed record XmipPromptSegment(XmipPromptPart[] Parts)
{
    /// <summary>The whole segment as text, for anything that wants a line.</summary>
    public string Text => string.Concat(Parts.Select(part => part.Text));

    /// <summary>The first part's color, the segment's color when it is one word.</summary>
    public ConsoleColor Color => Parts.Length > 0 ? Parts[0].Color : ConsoleColor.DarkGray;

    /// <summary>A segment of one part.</summary>
    public static XmipPromptSegment Plain(string text, ConsoleColor color)
    {
        return new XmipPromptSegment([new XmipPromptPart(text, color)]);
    }
}

/// <summary>
/// Follows Xmip in the background and exposes only an atomic cached segment to
/// PowerShell's prompt function. The prompt thread never touches the runtime.
/// </summary>
/// <remarks>
/// The surface the prompt reads is stated in <c>xmip.powershell.toml</c>
/// beside the module, through the same keys and the same choice as the GUI
/// hosts and the executable (ADR-0052 clause 3); nothing is guessed from an
/// environment variable or a file in a temp directory. A document that names
/// no surface leaves the one runtime-discovery rule — <c>RuntimeLibrary</c>,
/// else <c>XMIP_RUNTIME_LIBRARY</c>, else the library beside the module —
/// and the segment says so while nothing answers.
/// </remarks>
public static class PromptMonitor
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? _stop;
    private static Task? _worker;
    private static XmipPromptSegment _current = Connecting();

    // The snapshot the session said to follow, over the document's choice,
    // and which observer may write the segment: one that was replaced must
    // not overwrite what its successor published.
    private static string? _followed;
    private static int _generation;

    // How many clusters the session said are rolling beside the one followed.
    // The prompt follows one publication — a rollup over two clusters would be
    // a mood at a scope in neither tree — but it must not read as the whole
    // estate when it is half of it (ADR-0052, amendment 2026-09-20).
    private static int _beside;

    // What was last published and when this observer read it, so the next
    // rendering has an interval to divide by, and the rate it gave, so the
    // next one can say whether the rate is climbing (ADR-0052, amendment
    // 2026-09-20). One observer writes these, so they need no guard beyond
    // the generation's. The clock is the reader's: a published snapshot
    // carries no observation time for its counts, and the prompt asks at
    // every notice, so the interval between two reads is the interval
    // between two publications.
    private static Figures? _published;
    private static DateTimeOffset _readAt;
    private static FigureFlow? _flow;

    /// <summary>The latest cached segment; reading it cannot block.</summary>
    public static XmipPromptSegment Current => Volatile.Read(ref _current);

    /// <summary>Begin observing once. Safe to call on repeated imports.</summary>
    public static void Start()
    {
        lock (Gate)
        {
            if (_worker is { IsCompleted: false })
            {
                return;
            }

            Restart();
        }
    }

    /// <summary>
    /// Follow this snapshot from now on, in place of what the document names.
    /// The shipped document follows the roll started as cluster C1; a roll
    /// the operator starts under another name publishes elsewhere, and on
    /// 2026-09-18 the prompt sat frozen on C1 while CC1 rolled. The command
    /// that starts a roll knows the file and says so here: stated by the
    /// operator in the session, not guessed (ADR-0052 clause 3).
    /// </summary>
    public static void Follow(string snapshot)
    {
        Follow(snapshot, []);
    }

    /// <summary>
    /// Follow this snapshot, knowing these others are rolling beside it. The
    /// prompt reads one publication and says so: the segment names the cluster
    /// it is at and, where the session named more, how many it is not showing
    /// (ADR-0052, amendment 2026-09-20). Until then <c>Start-XmipTest</c>
    /// followed whichever roll started last and an operator with two could not
    /// tell the segment was one of them. The others are named, never counted
    /// from files on disk: a surface is stated (clause 3).
    /// </summary>
    public static void Follow(string snapshot, params string[] beside)
    {
        ArgumentNullException.ThrowIfNull(beside);

        lock (Gate)
        {
            string followed = Path.GetFullPath(snapshot);
            _followed = followed;
            _beside = beside
                .Where(other => !string.IsNullOrWhiteSpace(other))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count(other => !string.Equals(
                    other, followed, StringComparison.OrdinalIgnoreCase));
            _stop?.Cancel();
            Restart();
        }
    }

    // Under the gate: a new observer under a new generation.
    private static void Restart()
    {
        _stop?.Dispose();
        _stop = new CancellationTokenSource();
        int generation = Interlocked.Increment(ref _generation);
        _published = null;
        _flow = null;
        _readAt = default;
        CancellationToken stop = _stop.Token;
        Volatile.Write(ref _current, Connecting());
        _worker = Task.Run(() => ObserveAsync(generation, stop));
    }

    private static void Say(int generation, XmipPromptSegment segment)
    {
        if (generation == Volatile.Read(ref _generation))
        {
            Volatile.Write(ref _current, segment);
        }
    }

    /// <summary>End observation. The worker owns and later disposes its surface.</summary>
    public static void Stop()
    {
        lock (Gate)
        {
            _stop?.Cancel();
        }
    }

    /// <summary>
    /// Nothing at all, the way posh-git says nothing outside a repository.
    /// The owner, 2026-09-18: that Xmip is connected is obvious where the
    /// prompt shows figures, so connecting, unavailable and not configured
    /// are no words on the line; they are the absence of the segment.
    /// </summary>
    public static XmipPromptSegment Nothing { get; } = new([]);

    private static XmipPromptSegment Connecting()
    {
        return Nothing;
    }

    private static async Task ObserveAsync(int generation, CancellationToken stop)
    {
        IOperatorSurface? surface = null;

        try
        {
            surface = OpenSurface();

            await foreach (SurfaceChange _ in surface.WatchAsync(stop).ConfigureAwait(false))
            {
                try
                {
                    Publish(generation, surface);
                }
                catch (Exception failure) when (failure is not OperationCanceledException)
                {
                    // One publication that could not be read is one missed
                    // tick, and the segment keeps what it said. It used to
                    // end the observer: the prompt went blank and stayed so
                    // (2026-09-18, three rolls writing one file).
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Remove-Module is the normal end.
        }
        catch (InvalidOperationException)
        {
            // The document names a surface this build does not know, or a
            // snapshot with no path: SurfaceChoice refused it, as it should.
            Say(generation, XmipPromptSegment.Plain("[Xmip misconfigured]", ConsoleColor.DarkRed));
        }
        catch (Exception)
        {
            Say(generation, Nothing);
        }
        finally
        {
            if (surface is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    // The snapshot the session named, stated over the document, by the one
    // precedence the executable and every cmdlet share: the document's
    // choice and the first cluster where it names several — the prompt is
    // one line (ADR-0052, amendment 2026-09-20) — else the runtime rule, with
    // "beside the executable" read as beside the module.
    private static IOperatorSurface OpenSurface()
    {
        return ModuleSurface.Stated(new SurfaceLine(Snapshot: Volatile.Read(ref _followed)));
    }

    private static void Publish(int generation, IOperatorSurface surface)
    {
        ScopeIndex index = surface.Index();

        if (index.Leaves == 0)
        {
            Say(generation, Nothing);

            return;
        }

        Figures figures = surface.Figures(ScopeTree.Root);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        FigureFlow flow = FigureFlow.Between(
            figures, _published, _published is null ? TimeSpan.Zero : now - _readAt);
        int beside = Volatile.Read(ref _beside);
        Say(generation, SegmentRender.Render(index, figures, flow, _flow, beside));
        _published = figures;
        _readAt = now;

        // A publication that moved nothing leaves the rate as it was rather
        // than as unknown: the observer read twice, so the interval is real.
        _flow = flow.Known ? flow : _flow;
    }
}
