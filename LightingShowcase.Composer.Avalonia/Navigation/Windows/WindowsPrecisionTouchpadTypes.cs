/*
 * This UI code turns editor state into controls and converts user edits back into validated domain operations.
 * Dialog/window state is intentionally temporary: values should only become authoritative scene changes through
 * the session/controller path, which preserves cancel, undo, and renderer invalidation behavior.
 */
using System.Runtime.InteropServices;

namespace LightingShowcase.Composer.Navigation.Windows;

// WindowsPointerInputType makes a closed set of choices compiler-visible instead of passing loosely related
// integers or strings. Code that switches over Pointer, Touch, Pen, Mouse, Touchpad is where the behavioral meaning
// of each choice is implemented.
internal enum WindowsPointerInputType : uint
{
    Pointer = 1,
    Touch = 2,
    Pen = 3,
    Mouse = 4,
    Touchpad = 5
}

// WindowsPointerFlags makes a closed set of choices compiler-visible instead of passing loosely related integers or
// strings. Code that switches over None, New, InRange, InContact, FirstButton, SecondButton, ThirdButton,
// FourthButton, FifthButton, Primary is where the behavioral meaning of each choice is implemented.
[Flags]
internal enum WindowsPointerFlags : uint
{
    None = 0x00000000,
    New = 0x00000001,
    InRange = 0x00000002,
    InContact = 0x00000004,
    FirstButton = 0x00000010,
    SecondButton = 0x00000020,
    ThirdButton = 0x00000040,
    FourthButton = 0x00000080,
    FifthButton = 0x00000100,
    Primary = 0x00002000,
    Confidence = 0x00004000,
    Canceled = 0x00008000,
    Down = 0x00010000,
    Update = 0x00020000,
    Up = 0x00040000,
    Wheel = 0x00080000,
    HWheel = 0x00100000,
    CaptureChanged = 0x00200000,
    HasTransform = 0x00400000
}

// WindowsPointerButtonChangeType makes a closed set of choices compiler-visible instead of passing loosely related
// integers or strings. Code that switches over None, FirstButtonDown, FirstButtonUp, SecondButtonDown,
// SecondButtonUp, ThirdButtonDown, ThirdButtonUp, FourthButtonDown, FourthButtonUp, FifthButtonDown is where the
// behavioral meaning of each choice is implemented.
internal enum WindowsPointerButtonChangeType : uint
{
    None,
    FirstButtonDown,
    FirstButtonUp,
    SecondButtonDown,
    SecondButtonUp,
    ThirdButtonDown,
    ThirdButtonUp,
    FourthButtonDown,
    FourthButtonUp,
    FifthButtonDown,
    FifthButtonUp
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsNativePoint
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsNativeRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsPointerInfo
{
    public WindowsPointerInputType PointerType;
    public uint PointerId;
    public uint FrameId;
    public WindowsPointerFlags PointerFlags;
    public nint SourceDevice;
    public nint HwndTarget;
    public WindowsNativePoint PtPixelLocation;
    public WindowsNativePoint PtHimetricLocation;
    public WindowsNativePoint PtPixelLocationRaw;
    public WindowsNativePoint PtHimetricLocationRaw;
    public uint DwTime;
    public uint HistoryCount;
    public int InputData;
    public uint DwKeyStates;
    public ulong PerformanceCount;
    public WindowsPointerButtonChangeType ButtonChangeType;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsPointerTouchInfo
{
    public WindowsPointerInfo PointerInfo;
    public uint TouchFlags;
    public uint TouchMask;
    public WindowsNativeRect RcContact;
    public WindowsNativeRect RcContactRaw;
    public uint Orientation;
    public uint Pressure;
}

internal readonly record struct WindowsTouchContact(
    uint PointerId,
    uint FrameId,
    double X,
    double Y,
    WindowsPointerFlags Flags);
