// -----------------------------------------------------------------------------
// File: LightingShowcase.Composer.Avalonia/Navigation/Windows/WindowsPrecisionTouchpadApi.cs
// Purpose: P/Invoke bindings for the Windows precision touchpad API.
//
// Native method signatures and constants used to receive high-resolution touchpad input.
// -----------------------------------------------------------------------------

using System.ComponentModel;
using System.Runtime.InteropServices;

namespace LightingShowcase.Composer.Navigation.Windows;

/// <summary>
/// Direct Windows 11 Precision Touchpad API loader.
///
/// The newer touchpad-capable window APIs are resolved dynamically so the
/// application can still start on Windows versions that do not export them.
/// </summary>
internal sealed unsafe class WindowsPrecisionTouchpadApi : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool RegisterTouchpadCapableWindowDelegate(
        nint hwnd,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private unsafe delegate bool GetPointerFrameTouchpadInfoDelegate(
        uint pointerId,
        ref uint pointerCount,
        WindowsPointerTouchInfo* touchpadInfo);

    /// <summary>Declares the user32 GetPointerType import used to classify pointer input.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPointerType(
        uint pointerId,
        out WindowsPointerInputType pointerType);

    /// <summary>Declares the kernel32 LoadLibraryW import used to load user32 dynamically.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryW(string lpLibFileName);

    /// <summary>Declares the kernel32 GetProcAddress import used to resolve exports by name.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern nint GetProcAddress(nint hModule, string lpProcName);

    /// <summary>Declares the kernel32 GetProcAddress import used to resolve exports by ordinal.</summary>
    [DllImport("kernel32.dll", EntryPoint = "GetProcAddress", ExactSpelling = true)]
    private static extern nint GetProcAddressByOrdinal(nint hModule, nint ordinal);

    /// <summary>Declares the kernel32 FreeLibrary import used to release the loaded module.</summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint hLibModule);

    private readonly nint user32;
    private readonly RegisterTouchpadCapableWindowDelegate registerWindow;
    private readonly GetPointerFrameTouchpadInfoDelegate getFrame;
    private bool disposed;

    /// <summary>Stores the loaded user32 handle and resolved touchpad delegates.</summary>
    private WindowsPrecisionTouchpadApi(
        nint user32,
        RegisterTouchpadCapableWindowDelegate registerWindow,
        GetPointerFrameTouchpadInfoDelegate getFrame)
    {
        this.user32 = user32;
        this.registerWindow = registerWindow;
        this.getFrame = getFrame;
    }

    /// <summary>Loads user32 and resolves the Windows 11 precision touchpad entry points.</summary>
    public static WindowsPrecisionTouchpadApi Load()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new PlatformNotSupportedException("Native Precision Touchpad input requires Windows 11.");

        nint module = LoadLibraryW("user32.dll");
        if (module == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "LoadLibraryW(user32.dll) failed.");

        try
        {
            nint registerPtr = GetExport(module, "RegisterTouchpadCapableWindow", 2689);
            nint getFramePtr = GetExport(module, "GetPointerFrameTouchpadInfo", 2693);

            return new WindowsPrecisionTouchpadApi(
                module,
                Marshal.GetDelegateForFunctionPointer<RegisterTouchpadCapableWindowDelegate>(registerPtr),
                Marshal.GetDelegateForFunctionPointer<GetPointerFrameTouchpadInfoDelegate>(getFramePtr));
        }
        catch
        {
            FreeLibrary(module);
            throw;
        }
    }

    /// <summary>Resolves a user32 export by name, falling back to its ordinal.</summary>
    private static nint GetExport(nint module, string name, int ordinal)
    {
        nint address = GetProcAddress(module, name);
        if (address == 0)
            address = GetProcAddressByOrdinal(module, (nint)ordinal);

        if (address == 0)
        {
            throw new EntryPointNotFoundException(
                $"user32.dll does not expose {name} (ordinal {ordinal}).");
        }

        return address;
    }

    /// <summary>Registers a window handle for precision touchpad input.</summary>
    public void RegisterWindow(nint hwnd)
    {
        if (!registerWindow(hwnd, true))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "RegisterTouchpadCapableWindow failed.");
    }

    /// <summary>Unregisters a window handle from precision touchpad input.</summary>
    public void UnregisterWindow(nint hwnd)
    {
        if (hwnd != 0)
            registerWindow(hwnd, false);
    }

    /// <summary>Returns true when the given pointer id is a touchpad pointer.</summary>
    public bool IsTouchpadPointer(uint pointerId)
    {
        return GetPointerType(pointerId, out WindowsPointerInputType pointerType)
            && pointerType == WindowsPointerInputType.Touchpad;
    }

    /// <summary>Reads the active touch contacts for the given pointer id, returning false when no frame is available.</summary>
    public bool TryGetFrame(uint pointerId, out WindowsTouchContact[] contacts)
    {
        contacts = Array.Empty<WindowsTouchContact>();

        uint count = 0;
        getFrame(pointerId, ref count, null);
        if (count == 0)
            return false;

        int managedCount = checked((int)count);
        var native = new WindowsPointerTouchInfo[managedCount];
        uint capacity = count;

        fixed (WindowsPointerTouchInfo* buffer = native)
        {
            if (!getFrame(pointerId, ref capacity, buffer))
                return false;
        }

        int written = checked((int)capacity);
        var result = new WindowsTouchContact[written];
        for (int i = 0; i < written; i++)
        {
            ref readonly WindowsPointerInfo info = ref native[i].PointerInfo;
            WindowsNativePoint p = info.PtHimetricLocationRaw;

            result[i] = new WindowsTouchContact(
                info.PointerId,
                info.FrameId,
                p.X,
                p.Y,
                info.PointerFlags);
        }

        contacts = result;
        return true;
    }

    /// <summary>Releases the loaded user32 module.</summary>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (user32 != 0)
            FreeLibrary(user32);
    }
}
