using System.Runtime.InteropServices;

namespace Viciotek.Infrastructure.Input;

[StructLayout(LayoutKind.Explicit)]
public struct XInputGamepadState
{
    [FieldOffset(0)] public int Connected;
    [FieldOffset(4)] public int Flags;
    [FieldOffset(8)] public int VibrationLeft;
    [FieldOffset(12)] public int VibrationRight;
    [FieldOffset(16)] public short AxisLeftX;
    [FieldOffset(18)] public short AxisLeftY;
    [FieldOffset(20)] public short AxisRightX;
    [FieldOffset(22)] public short AxisRightY;
    [FieldOffset(24)] public short AxisLeftTrigger;
    [FieldOffset(26)] public short AxisRightTrigger;
    [FieldOffset(28)] public ushort Buttons;
}

public static class XInputGamepad
{
    public const int PlayerIndex = 0;

    [DllImport("xinput1_4.dll", SetLastError = true)]
    private static extern int XInputGetState(int dwUserIndex, ref XInputGamepadState state);

    public static XInputGamepadState GetState(int playerIndex = PlayerIndex)
    {
        var current = new XInputGamepadState();
        var result = XInputGetState(playerIndex, ref current);
        return new XInputGamepadState
        {
            Connected = result == 0 ? 1 : 0,
            Flags = current.Flags,
            Buttons = current.Buttons,
            AxisLeftX = current.AxisLeftX,
            AxisLeftY = current.AxisLeftY,
            AxisRightX = current.AxisRightX,
            AxisRightY = current.AxisRightY,
            AxisLeftTrigger = current.AxisLeftTrigger,
            AxisRightTrigger = current.AxisRightTrigger,
        };
    }
}
