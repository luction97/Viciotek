namespace Viciotek.Domain;

/// <summary>
/// Movement direction derived from the left stick or the D-pad of a gamepad.
/// Pure input rule: D-pad takes precedence over the left stick, which has a dead zone.
/// </summary>
public enum GamepadMoveDirection
{
    None = 0,
    Up = 1,
    Down = 2,
    Left = 3,
    Right = 4,
}

public static class GamepadInput
{
    /// <summary>Dead zone in XInput left-stick units (half of half-scale).</summary>
    public const int DeadZone = 8192;

    /// <summary>XInput gamepad buttons mask for the D-pad (bits 0-3).</summary>
    public const ushort DpadMask = 0x000F;

    /// <summary>XInput gamepad button used to launch the selected game.</summary>
    public const ushort LaunchButton = 0x0010;

    public static GamepadMoveDirection ResolveMoveDirection(short stickX, short stickY, ushort dpadButtons)
    {
        var dpad = dpadButtons & DpadMask;
        if (dpad > 0)
        {
            return dpad switch
            {
                0x0001 => GamepadMoveDirection.Up,
                0x0002 => GamepadMoveDirection.Down,
                0x0004 => GamepadMoveDirection.Left,
                0x0008 => GamepadMoveDirection.Right,
                _ => GamepadMoveDirection.None
            };
        }

        if (Math.Abs(stickX) <= DeadZone && Math.Abs(stickY) <= DeadZone)
        {
            return GamepadMoveDirection.None;
        }

        if (Math.Abs(stickY) >= Math.Abs(stickX))
        {
            return stickY < 0 ? GamepadMoveDirection.Up : GamepadMoveDirection.Down;
        }

        return stickX < 0 ? GamepadMoveDirection.Left : GamepadMoveDirection.Right;
    }

    /// <summary>Converts a WinMM axis (0-65535) to a signed XInput-like value.</summary>
    public static int WinMMToSigned(uint axis) => (int)axis - 32768;

    /// <summary>Resolves direction from a WinMM (multimedia) joystick: POV hat or X/Y axes with dead zone.</summary>
    public static GamepadMoveDirection ResolveMoveDirectionWinMM(uint x, uint y, uint pov)
    {
        if (pov != 0xFFFFFFFF)
        {
            var degrees = (int)(pov / 100);
            if (degrees <= 45 || degrees >= 315) return GamepadMoveDirection.Up;
            if (degrees >= 45 && degrees <= 135) return GamepadMoveDirection.Right;
            if (degrees >= 135 && degrees <= 225) return GamepadMoveDirection.Down;
            if (degrees >= 225 && degrees <= 315) return GamepadMoveDirection.Left;
        }

        var sx = WinMMToSigned(x);
        var sy = WinMMToSigned(y);

        if (Math.Abs(sx) <= DeadZone && Math.Abs(sy) <= DeadZone)
        {
            return GamepadMoveDirection.None;
        }

        if (Math.Abs(sy) >= Math.Abs(sx))
        {
            return sy < 0 ? GamepadMoveDirection.Up : GamepadMoveDirection.Down;
        }

        return sx < 0 ? GamepadMoveDirection.Left : GamepadMoveDirection.Right;
    }
}
