using System.Globalization;
using Microsoft.Extensions.Configuration;
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
    /// <summary>The module's configuration document, beside it.</summary>
    public const string ConfigurationFile = "xmip.powershell.toml";

    private static readonly object Gate = new();
    private static CancellationTokenSource? _stop;
    private static Task? _worker;
    private static XmipPromptSegment _current = Connecting();

    // The snapshot the session said to follow, over the document's choice,
    // and which observer may write the segment: one that was replaced must
    // not overwrite what its successor published.
    private static string? _followed;
    private static int _generation;

    // What was last published, for the next rendering to compare with; one
    // observer writes it, so it needs no guard beyond the generation's.
    private static Figures? _published;

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
        lock (Gate)
        {
            _followed = Path.GetFullPath(snapshot);
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

    /// <summary>The console's nearest color to the estate's name for a mood's
    /// color (<see cref="English.Color"/>): sixteen colors stand in for the
    /// stylesheet's tokens, and the word decides which.</summary>
    public static ConsoleColor Paint(string color)
    {
        return color switch
        {
            "green" => ConsoleColor.Green,
            "slate" => ConsoleColor.DarkGray,
            "blue" => ConsoleColor.Blue,
            "yellow" => ConsoleColor.Yellow,
            "burnt" => ConsoleColor.DarkRed,
            "orange" => ConsoleColor.DarkYellow,
            "red" => ConsoleColor.Red,
            _ => ConsoleColor.DarkGray,
        };
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
            surface = OpenSurface(out bool configured);

            await foreach (SurfaceChange _ in surface.WatchAsync(stop).ConfigureAwait(false))
            {
                Publish(generation, surface, configured);
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

    private static IOperatorSurface OpenSurface(out bool configured)
    {
        if (Volatile.Read(ref _followed) is { } followed)
        {
            configured = true;
            return new SnapshotOperator(followed);
        }

        string moduleDirectory = Path.GetDirectoryName(typeof(PromptMonitor).Assembly.Location)
            ?? AppContext.BaseDirectory;
        IConfigurationRoot document =
            TomlDocument.Read(Path.Combine(moduleDirectory, ConfigurationFile));

        configured = SurfaceChoice.IsChosen(document);

        if (configured)
        {
            return SurfaceChoice.Open(document, moduleDirectory);
        }

        // Nothing named: the one rule, with "beside the executable" read as
        // beside the module, since the executable is pwsh itself.
        return new NativeOperator(RuntimeLibrary.Choose(
            document[RuntimeLibrary.ConfigurationKey],
            Environment.GetEnvironmentVariable(RuntimeLibrary.EnvironmentVariable),
            moduleDirectory,
            moduleDirectory));
    }

    private static void Publish(int generation, IOperatorSurface surface, bool configured)
    {
        ScopeIndex index = surface.Index();

        if (index.Leaves == 0)
        {
            _ = configured;
            Say(generation, Nothing);

            return;
        }

        Figures figures = surface.Figures(ScopeTree.Root);
        Say(generation, SegmentRender.Render(index, figures, _published));
        _published = figures;
    }
}
