using System.Runtime.InteropServices;

namespace Viciotek.Infrastructure.Input;

[StructLayout(LayoutKind.Sequential)]
public struct WinMMJoystickState
{
    public int Connected;
    public uint X;
    public uint Y;
    public uint Z;
    public uint R;
    public uint U;
    public uint V;
    public uint Buttons;
    public uint ButtonNumber;
    public uint Pov;
}

public static class WinMMJoystick
{
    public const uint JoyReturnAll = 0x000000FF;
    public const uint JoyReturnBasic = 0x000000C3; // X|Y|Buttons|POV
    public const uint JoyReturnPov = 0x00000040;
    public const uint JoyErrNoError = 0;
    public const uint JoyPovCentered = 0xFFFFFFFF;
    public const ushort LaunchButton = 0x0001; // Button 1

    [StructLayout(LayoutKind.Sequential)]
    private struct JoyInfoEx
    {
        public uint dwSize;
        public uint dwFlags;
        public uint dwXpos;
        public uint dwYpos;
        public uint dwZpos;
        public uint dwRpos;
        public uint dwUpos;
        public uint dwVpos;
        public uint dwButtons;
        public uint dwButtonNumber;
        public uint dwPOV;
        public uint dwReserved1;
        public uint dwReserved2;
    }

    [DllImport("winmm.dll")]
    private static extern uint joyGetNumDevs();

    [DllImport("winmm.dll")]
    private static extern uint joyGetPosEx(uint uJoyID, ref JoyInfoEx pji);

    public static WinMMJoystickState GetState()
    {
        var count = joyGetNumDevs();
        for (uint i = 0; i < count; i++)
        {
            foreach (var flags in new[] { JoyReturnAll, JoyReturnBasic })
            {
                var info = new JoyInfoEx
                {
                    dwSize = (uint)Marshal.SizeOf<JoyInfoEx>(),
                    dwFlags = flags
                };
                var result = joyGetPosEx(i, ref info);
                if (result == JoyErrNoError)
                {
                    return new WinMMJoystickState
                    {
                        Connected = 1,
                        X = info.dwXpos,
                        Y = info.dwYpos,
                        Z = info.dwZpos,
                        R = info.dwRpos,
                        U = info.dwUpos,
                        V = info.dwVpos,
                        Buttons = info.dwButtons,
                        ButtonNumber = info.dwButtonNumber,
                        Pov = info.dwPOV
                    };
                }
            }
        }
        return new WinMMJoystickState { Connected = 0 };
    }
}
