using System.Runtime.InteropServices;
using System.Text;

namespace Xmip.PowerShell.Interop;

/// <summary>
/// The Xmip module boundary, as declared by <c>include/xmip_module.h</c> in
/// xmip-core-abi.
/// </summary>
/// <remarks>
/// ADR-0012 clause 1: the header is normative and this is not. Nothing here
/// may be relied on where the header disagrees — if the two ever differ, the
/// header is right and this is a defect.
/// </remarks>
internal static class XmipAbi
{
    /// <summary>The handshake version this build speaks.</summary>
    public const uint AbiVersion = 1u;

    /// <summary>The one symbol every module exports.</summary>
    public const string Entrypoint = "xmip_create_module_v1";

    /// <summary>
    /// Platform naming for a loadable module, from section 1 of the header.
    /// </summary>
    public static string LibraryFileName(string moduleName) =>
        OperatingSystem.IsWindows() ? $"{moduleName}.dll"
        : OperatingSystem.IsMacOS() ? $"lib{moduleName}.dylib"
        : $"lib{moduleName}.so";
}

/// <summary>
/// A borrowed byte range. Never owned by the receiver, never null-terminated.
/// Valid only for the duration of the call it was passed to.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct XmipStr
{
    public readonly byte* Ptr;
    public readonly nuint Len;

    public XmipStr(byte* ptr, nuint len)
    {
        Ptr = ptr;
        Len = len;
    }

    /// <summary>
    /// Copies out of the borrow. The pointer stops being valid when the call
    /// that produced it returns, so nothing may hold onto it.
    /// </summary>
    public string Read()
    {
        if (Ptr is null || Len == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(Ptr, checked((int)Len));
    }
}

/// <summary>What the module says it is. Section 4 of the header.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XmipModuleDescriptor
{
    public uint AbiVersion;
    public XmipStr Provider;
    public XmipStr Module;
    public XmipStr Standard;
    public uint TraitMajor;
    public uint TraitMinor;
    public uint ModuleMajor;
    public uint ModuleMinor;
    public uint ModulePatch;
}

/// <summary>
/// What a module may call back into. Deliberately small — no allocator, no
/// thread pool, no clock, no configuration store.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XmipHost
{
    public uint AbiVersion;
    public void* Ctx;
    public delegate* unmanaged[Cdecl]<void*, int, XmipStr, XmipStr, void> Log;
    public delegate* unmanaged[Cdecl]<void*, int> Cancelled;
    public delegate* unmanaged[Cdecl]<void*, XmipStr> JourneyId;
}

/// <summary>The module handle. Section 7 of the header.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XmipModule
{
    public XmipModuleDescriptor Descriptor;
    public void* State;
    public void* Vtable;
    public delegate* unmanaged[Cdecl]<void*, XmipStr> LastError;
    public delegate* unmanaged[Cdecl]<void*, void> Destroy;
}
