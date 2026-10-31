using System.Runtime.InteropServices;

namespace Viciotek.Infrastructure.Input;

[StructLayout(LayoutKind.Sequential)]
public struct DirectInputJoystickState
{
    public int Connected;
    public int AxisX;
    public int AxisY;
    public int AxisZ;
    public int[] Pov; // 4 POV hats
    public byte[] Buttons; // up to 128
    public int ButtonCount;
}

public static class DirectInputJoystick
{
    private const uint DIRECTINPUT_VERSION = 0x0800;
    private const int DI_OK = 0;
    private const int DIEDFL_ATTACHEDONLY = 0x00000001;
    private const int DIENUM_CONTINUE = 1;
    private const int DIENUM_STOP = 0;

    private static readonly Guid CLSID_DirectInput8 = new("25E609E4-B259-11CF-BFC7-444553540000");
    private static Guid IID_IDirectInput8W = new("BF798031-483A-CE42-BCA6-9B99486052E0");
    private static readonly Guid GUID_Joystick = new("6F1D2B61-D5A0-11CF-BFC7-444553540000");
    private static readonly Guid GUID_SysKeyboard = new("6F1D2B60-D5A0-11CF-BFC7-444553540000");
    private static readonly Guid GUID_XAxis = new("A36D02E0-C9F3-11CF-BFC7-444553540000");
    private static readonly Guid GUID_YAxis = new("A36D02E1-C9F3-11CF-BFC7-444553540000");
    private static readonly Guid GUID_ZAxis = new("A36D02E2-C9F3-11CF-BFC7-444553540000");

    [DllImport("dinput8.dll")]
    private static extern int DirectInput8Create(IntPtr hinst, uint dwVersion, ref Guid riidltf, out IntPtr ppvOut, IntPtr punkOuter);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    // IDirectInput8 vtable indices (after IUnknown: QI, AddRef, Release)
    // 3: CreateDevice, 4: EnumDevices, 5: GetDeviceStatus, 6: RunControlPanel, 7: Initialize, 8: FindDevice, 9: EnumDevicesBySemantics, 10: ConfigureDevices

    // IDirectInputDevice8 vtable indices (after IUnknown)
    // 3: GetCapabilities, 4: EnumObjects, 5: GetProperty, 6: SetProperty, 7: Acquire, 8: Unacquire, 9: GetDeviceState, 10: GetDeviceData, 11: SetDataFormat, 12: SetEventNotification, 13: SetCooperativeLevel, 14: GetObjectInfo, 15: GetDeviceInfo, 16: RunControlPanel, 17: Initialize

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumDevicesCallback(ref DIDEVICEINSTANCE lpddi, IntPtr pvRef);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct DIDEVICEINSTANCE
    {
        public int dwSize;
        public Guid guidInstance;
        public Guid guidProduct;
        public int dwDevType;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string tszInstanceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string tszProductName;
        public Guid guidFFDriver;
        public ushort wUsagePage;
        public ushort wUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DIOBJECTDATAFORMAT
    {
        public IntPtr pguid;
        public int dwOfs;
        public int dwType;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DIDATAFORMAT
    {
        public int dwSize;
        public int dwObjSize;
        public int dwFlags;
        public int dwDataSize;
        public int dwNumObjs;
        public IntPtr rgodf;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DIJOYSTATE2
    {
        public int lX;
        public int lY;
        public int lZ;
        public int lRx;
        public int lRy;
        public int lRz;
        public int rglSlider0;
        public int rglSlider1;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
        public int[] rgdwPOV;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
        public byte[] rgbButtons;
    }

    private static IntPtr _directInput;
    private static IntPtr _device;
    private static DIDATAFORMAT _joystickFormat;
    private static IntPtr _rgodf;
    private static bool _initialized;
    private static bool _deviceFound;

    public static void Initialize(IntPtr hwnd)
    {
        if (_initialized) return;
        _initialized = true;

        var hinst = GetModuleHandle(null);
        int hr = DirectInput8Create(hinst, DIRECTINPUT_VERSION, ref IID_IDirectInput8W, out _directInput, IntPtr.Zero);
        if (hr != DI_OK || _directInput == IntPtr.Zero) return;

        // Enumerate devices
        var enumCallback = new EnumDevicesCallback((ref DIDEVICEINSTANCE instance, IntPtr pvRef) =>
        {
            // Only joystick/gamepad type
            if ((instance.dwDevType & 0xFF) != 4) // DIDEVTYPE_JOYSTICK = 4
                return DIENUM_CONTINUE;

            // Create device
            var guid = instance.guidInstance;
            hr = CreateDevice(_directInput, ref guid, out _device, IntPtr.Zero);
            if (hr != DI_OK || _device == IntPtr.Zero) return DIENUM_CONTINUE;

            _deviceFound = true;

            // Set data format
            BuildJoystickFormat();
            hr = SetDataFormat(_device, ref _joystickFormat);
            if (hr != DI_OK) { ReleaseDevice(); return DIENUM_STOP; }

            // Set cooperative level (DISCL_NONEXCLUSIVE | DISCL_BACKGROUND = 0x00000005)
            hr = SetCooperativeLevel(_device, hwnd, 0x00000005);
            if (hr != DI_OK) { ReleaseDevice(); return DIENUM_STOP; }

            // Acquire
            hr = Acquire(_device);
            // Acquire may fail if already acquired or no focus; continue anyway

            return DIENUM_STOP;
        });

        // EnumDevices: dwDevType = DIDEVTYPE_JOYSTICK (4), dwFlags = DIEDFL_ATTACHEDONLY
        hr = EnumDevices(_directInput, 4, enumCallback, IntPtr.Zero, DIEDFL_ATTACHEDONLY);
    }

    private static void BuildJoystickFormat()
    {
        // Build DIDATAFORMAT for DIJOYSTATE2
        int sizeOfObj = Marshal.SizeOf<DIOBJECTDATAFORMAT>();
        int numObjs = 2 + 2 + 2 + 2 + 2 + 2 + 2 + 2 + 4 + 128; // approximate, but we'll use standard layout
        // Actually, use the standard c_dfDIJoystick2 format which has 2 + 6 axes + 2 sliders + 4 POV + 128 buttons = 143 objects
        // For simplicity, define the objects manually
        numObjs = 143; // 2 (X,Y) + 6 axes (Z,Rx,Ry,Rz,Slider0,Slider1) + 4 POV + 128 buttons... actually let me count properly

        // Standard DIJOYSTATE2 data format objects:
        // X, Y, Z, Rx, Ry, Rz, Slider0, Slider1 = 8 axes
        // 4 POV hats
        // 128 buttons
        // Total = 8 + 4 + 128 = 140 objects
        numObjs = 140;

        _rgodf = Marshal.AllocHGlobal(sizeOfObj * numObjs);
        var rgodf = new DIOBJECTDATAFORMAT[numObjs];

        int offset = 0;
        // Axes: X, Y, Z, Rx, Ry, Rz, Slider0, Slider1
        Guid[] axisGuids = { GUID_XAxis, GUID_YAxis, GUID_ZAxis, GUID_XAxis, GUID_XAxis, GUID_XAxis, GUID_XAxis, GUID_XAxis };
        // We'll just use offsets; actual GUIDs matter less for state reading
        for (int i = 0; i < 8; i++)
        {
            rgodf[i].pguid = IntPtr.Zero; // use default
            rgodf[i].dwOfs = offset;
            rgodf[i].dwType = 0x00000002 | (i << 8); // DIDFT_ABSAXIS | axis index
            rgodf[i].dwFlags = 0;
            offset += 4;
        }
        // POV hats
        for (int i = 0; i < 4; i++)
        {
            rgodf[8 + i].pguid = IntPtr.Zero;
            rgodf[8 + i].dwOfs = offset;
            rgodf[8 + i].dwType = 0x00000010 | ((i + 1) << 8); // DIDFT_POV
            rgodf[8 + i].dwFlags = 0;
            offset += 4;
        }
        // Buttons
        for (int i = 0; i < 128; i++)
        {
            rgodf[8 + 4 + i].pguid = IntPtr.Zero;
            rgodf[8 + 4 + i].dwOfs = offset;
            rgodf[8 + 4 + i].dwType = 0x0000000C | ((i + 1) << 8); // DIDFT_BUTTON
            rgodf[8 + 4 + i].dwFlags = 0;
            offset += 1;
        }

        for (int i = 0; i < numObjs; i++)
        {
            Marshal.StructureToPtr(rgodf[i], IntPtr.Add(_rgodf, i * sizeOfObj), false);
        }

        _joystickFormat = new DIDATAFORMAT
        {
            dwSize = Marshal.SizeOf<DIDATAFORMAT>(),
            dwObjSize = sizeOfObj,
            dwFlags = 0, // DIDF_ABSAXIS
            dwDataSize = Marshal.SizeOf<DIJOYSTATE2>(),
            dwNumObjs = numObjs,
            rgodf = _rgodf
        };
    }

    private static void ReleaseDevice()
    {
        if (_device != IntPtr.Zero)
        {
            // Unacquire
            Unacquire(_device);
            // Release
            var releaseDelegate = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(_device), 2 * IntPtr.Size));
            releaseDelegate(_device);
            _device = IntPtr.Zero;
        }
    }

    public static DirectInputJoystickState GetState()
    {
        if (!_initialized || !_deviceFound || _device == IntPtr.Zero)
            return new DirectInputJoystickState { Connected = 0 };

        var state = new DIJOYSTATE2
        {
            rgdwPOV = new int[4],
            rgbButtons = new byte[128]
        };
        int hr = GetDeviceState(_device, Marshal.SizeOf<DIJOYSTATE2>(), ref state);
        if (hr != DI_OK)
        {
            // Try to re-acquire
            Acquire(_device);
            hr = GetDeviceState(_device, Marshal.SizeOf<DIJOYSTATE2>(), ref state);
            if (hr != DI_OK)
                return new DirectInputJoystickState { Connected = 0 };
        }

        var result = new DirectInputJoystickState
        {
            Connected = 1,
            AxisX = state.lX,
            AxisY = state.lY,
            AxisZ = state.lZ,
            Pov = state.rgdwPOV,
            Buttons = state.rgbButtons,
            ButtonCount = 128
        };
        return result;
    }

    public static void Shutdown()
    {
        ReleaseDevice();
        if (_directInput != IntPtr.Zero)
        {
            var releaseDelegate = Marshal.GetDelegateForFunctionPointer<ReleaseDelegate>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(_directInput), 2 * IntPtr.Size));
            releaseDelegate(_directInput);
            _directInput = IntPtr.Zero;
        }
        if (_rgodf != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_rgodf);
            _rgodf = IntPtr.Zero;
        }
        _initialized = false;
        _deviceFound = false;
    }

    // Vtable call helpers
    private static int CreateDevice(IntPtr di, ref Guid guid, out IntPtr device, IntPtr outer)
    {
        var vtable = Marshal.ReadIntPtr(di);
        var funcPtr = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<CreateDeviceDelegate>(funcPtr);
        return del(di, ref guid, out device, outer);
    }

    private static int EnumDevices(IntPtr di, int devType, EnumDevicesCallback callback, IntPtr pvRef, int flags)
    {
        var vtable = Marshal.ReadIntPtr(di);
        var funcPtr = Marshal.ReadIntPtr(vtable, 4 * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<EnumDevicesDelegate>(funcPtr);
        return del(di, devType, callback, pvRef, flags);
    }

    private static int SetDataFormat(IntPtr device, ref DIDATAFORMAT format)
    {
        var vtable = Marshal.ReadIntPtr(device);
        var funcPtr = Marshal.ReadIntPtr(vtable, 11 * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<SetDataFormatDelegate>(funcPtr);
        return del(device, ref format);
    }

    private static int SetCooperativeLevel(IntPtr device, IntPtr hwnd, int flags)
    {
        var vtable = Marshal.ReadIntPtr(device);
        var funcPtr = Marshal.ReadIntPtr(vtable, 13 * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<SetCooperativeLevelDelegate>(funcPtr);
        return del(device, hwnd, flags);
    }

    private static int Acquire(IntPtr device)
    {
        var vtable = Marshal.ReadIntPtr(device);
        var funcPtr = Marshal.ReadIntPtr(vtable, 7 * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<AcquireDelegate>(funcPtr);
        return del(device);
    }

    private static int Unacquire(IntPtr device)
    {
        var vtable = Marshal.ReadIntPtr(device);
        var funcPtr = Marshal.ReadIntPtr(vtable, 8 * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<UnacquireDelegate>(funcPtr);
        return del(device);
    }

    private static int GetDeviceState(IntPtr device, int size, ref DIJOYSTATE2 state)
    {
        var vtable = Marshal.ReadIntPtr(device);
        var funcPtr = Marshal.ReadIntPtr(vtable, 9 * IntPtr.Size);
        var del = Marshal.GetDelegateForFunctionPointer<GetDeviceStateDelegate>(funcPtr);
        return del(device, size, ref state);
    }

    // Delegates
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDeviceDelegate(IntPtr di, ref Guid rguid, out IntPtr lplpDirectInputDevice, IntPtr pUnkOuter);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int EnumDevicesDelegate(IntPtr di, int dwDevType, EnumDevicesCallback lpCallback, IntPtr pvRef, int dwFlags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetDataFormatDelegate(IntPtr device, ref DIDATAFORMAT lpdf);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int SetCooperativeLevelDelegate(IntPtr device, IntPtr hwnd, int dwFlags);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int AcquireDelegate(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int UnacquireDelegate(IntPtr device);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetDeviceStateDelegate(IntPtr device, int cbData, ref DIJOYSTATE2 lpvData);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ReleaseDelegate(IntPtr punk);
}
