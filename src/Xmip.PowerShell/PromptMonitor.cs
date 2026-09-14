using Microsoft.Extensions.Configuration;
using Xmip.Abi.Operate;
using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>A prompt-safe rendering of the latest operator snapshot.</summary>
public sealed record XmipPromptSegment(string Text, ConsoleColor Color);

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

            _stop?.Dispose();
            _stop = new CancellationTokenSource();
            _current = Connecting();
            _worker = Task.Run(() => ObserveAsync(_stop.Token));
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

    private static XmipPromptSegment Connecting()
    {
        return new XmipPromptSegment("[Xmip connecting]", ConsoleColor.DarkGray);
    }

    private static async Task ObserveAsync(CancellationToken stop)
    {
        IOperatorSurface? surface = null;

        try
        {
            surface = OpenSurface(out bool configured);

            await foreach (SurfaceChange _ in surface.WatchAsync(stop).ConfigureAwait(false))
            {
                Publish(surface, configured);
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
            Volatile.Write(
                ref _current,
                new XmipPromptSegment("[Xmip misconfigured]", ConsoleColor.DarkRed));
        }
        catch (Exception)
        {
            Volatile.Write(
                ref _current,
                new XmipPromptSegment("[Xmip unavailable]", ConsoleColor.DarkRed));
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

    private static void Publish(IOperatorSurface surface, bool configured)
    {
        IReadOnlyList<HealthRecord> records = surface.Health(ScopeTree.Root);

        if (records.Count == 0)
        {
            string nothing = configured ? "unavailable" : "not configured";
            Volatile.Write(
                ref _current, new XmipPromptSegment($"[Xmip {nothing}]", ConsoleColor.DarkGray));

            return;
        }

        HealthState state = ScopeTree.Rollup(records) ?? HealthState.Done;

        Volatile.Write(
            ref _current,
            new XmipPromptSegment($"[Xmip {English.Mood(state)}]", Paint(English.Color(state))));
    }
}
