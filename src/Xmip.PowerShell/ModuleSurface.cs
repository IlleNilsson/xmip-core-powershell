using Xmip.Surface;

namespace Xmip.PowerShell;

/// <summary>
/// Where the module's own document lies and what it chooses. Everything else
/// about choosing a surface is <c>Xmip.Surface</c>'s, the same for the
/// executable as for this module (ADR-0052 clause 1): the prompt and every
/// cmdlet that reads a scope open their surface here, by
/// <see cref="SurfaceChoice.Stated"/>.
/// </summary>
public static class ModuleSurface
{
    /// <summary>The module's configuration document, beside it.</summary>
    public const string ConfigurationFile = "xmip.powershell.toml";

    /// <summary>
    /// The directory the module was loaded from. Beside the module is where
    /// the document lies and, by the one discovery rule, where the runtime
    /// library lies by default: the executable is pwsh itself, so "beside the
    /// executable" is read as beside the module.
    /// </summary>
    public static string Directory =>
        Path.GetDirectoryName(typeof(ModuleSurface).Assembly.Location) ?? AppContext.BaseDirectory;

    /// <summary>The full path of the module's document.</summary>
    public static string DocumentPath => Path.Combine(Directory, ConfigurationFile);

    /// <summary>The surface the line states over the document, once it has
    /// answered; null with the reason otherwise.</summary>
    public static IOperatorSurface? Answering(SurfaceLine line, out string reason)
    {
        return SurfaceChoice.Answering(line, DocumentPath, Directory, out reason);
    }

    /// <summary>The surface the line states over the document, not yet asked
    /// to answer — what the prompt follows and watches until it does.</summary>
    /// <exception cref="InvalidOperationException">The document names a
    /// surface this build does not know.</exception>
    public static IOperatorSurface Stated(SurfaceLine line)
    {
        return SurfaceChoice.Stated(line, TomlDocument.Read(DocumentPath), Directory, Directory);
    }

    /// <summary>The runtime library a cmdlet that asks a runtime loads:
    /// <c>-Library</c>, else the document, the environment and beside the
    /// module.</summary>
    public static string Runtime(string? library)
    {
        return RuntimeLibrary.Stated(
            library, TomlDocument.Read(DocumentPath), Directory, Directory);
    }
}
