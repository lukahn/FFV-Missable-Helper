using System;
using System.Collections.Generic;
using UnityEngine;

namespace FFVMissableHelper;

internal enum GameKey
{
    Up,
    Down,
    Left,
    Right,
    Confirm,
    Cancel
}

/// <summary>
/// Key reads, combining every source that can answer instead of trusting one.
///
/// The game has its own input manager (Il2CppSystem.Input.InputSystemManager) which knows
/// about its key configs and controllers - but it is not guaranteed to answer from outside
/// the game's own update, and when it silently says "no" the plugin never sees the press.
/// So the manager is asked first, then whatever the engine reports directly. Whichever
/// source answered is traced once, so the log says which one is real on a given machine.
/// </summary>
internal static class InputCompat
{
    private static Il2CppSystem.Input.InputSystemManager _manager;
    private static int _lookupFrame = -1;
    private static bool _lookupFailed;

    // one answer per key per frame, so callers in different Updates cannot disagree
    private static int _frame = -1;
    private static readonly Dictionary<GameKey, bool> Cache = new();

    private static readonly HashSet<string> TracedSources = new();
    private static bool _legacyUnavailable;
    private static bool _axesUnavailable;
    private static readonly Dictionary<GameKey, float> AxisEdge = new();

    // the last raw answer per key, so a held button is one action rather than a stream
    private static readonly Dictionary<GameKey, bool> Held = new();

    // held-direction repeat, in the style of the game's own menus: one step, a pause, then a
    // steady scroll for as long as the key stays down
    private const float RepeatDelay = 0.40f;
    private const float RepeatInterval = 0.08f;

    private static readonly Dictionary<GameKey, bool> DirectionDown = new();
    private static readonly Dictionary<GameKey, float> NextRepeat = new();

    public static bool Down(GameKey key)
    {
        int frame = Time.frameCount;
        if (frame != _frame)
        {
            _frame = frame;
            Cache.Clear();
        }

        if (Cache.TryGetValue(key, out bool cached))
            return cached;

        bool result = key == GameKey.Up || key == GameKey.Down
            ? Direction(key, out string source)
            : Single(key, out source);

        Cache[key] = result;

        if (result && source != null && Plugin.Verbose.Value && TracedSources.Add(source))
            Plugin.Trace($"input: {key} came from {source}");

        return result;
    }

    /// <summary>
    /// A held direction: one step on the frame it goes down, then a repeat every
    /// RepeatInterval once RepeatDelay has passed. This is what makes holding an arrow key
    /// scroll the list instead of moving a single row.
    /// </summary>
    private static bool Direction(GameKey key, out string source)
    {
        float now = Time.unscaledTime;

        bool held = DirectionHeld(key);
        bool pulse = Read(key, out source);

        bool wasDown = DirectionDown.TryGetValue(key, out bool previous) && previous;
        DirectionDown[key] = held;

        if (!wasDown)
        {
            // the press itself: either source may be the one that saw it
            NextRepeat[key] = now + RepeatDelay;
            return held || pulse;
        }

        if (!held)
        {
            // released this frame; a stray pulse still counts once
            NextRepeat[key] = now + RepeatDelay;
            return pulse;
        }

        if (now < NextRepeat[key])
            return false;

        NextRepeat[key] = now + RepeatInterval;
        return true;
    }

    /// <summary>
    /// One action per press. Sources disagree about repeats (the game's own manager reports
    /// the whole hold, GetKeyDown is a pulse), so latch here: only a frame where the key was
    /// previously up counts.
    /// </summary>
    private static bool Single(GameKey key, out string source)
    {
        bool raw = Read(key, out source);

        bool wasHeld = Held.TryGetValue(key, out bool previous) && previous;
        Held[key] = raw;

        return raw && !wasHeld;
    }

    /// <summary>
    /// True while the direction is held down, from whichever source can answer. Separate from
    /// <see cref="Read"/>, which only reports the edge, because a repeat needs the state.
    /// </summary>
    private static bool DirectionHeld(GameKey key)
    {
        if (key != GameKey.Up && key != GameKey.Down)
            return false;

        var manager = Manager();
        if (manager != null)
        {
            try
            {
                if (manager.GetKeyDown(Action(key)))
                    return true;
            }
            catch (Exception e)
            {
                LogOnce("manager-hold", $"[Input] the game's held read failed: {e.Message}");
            }
        }

        if (_legacyUnavailable)
            return false;

        try
        {
            if (UnityEngine.Input.GetKey(key == GameKey.Up ? KeyCode.UpArrow : KeyCode.DownArrow))
                return true;

            if (UnityEngine.Input.GetKey(key == GameKey.Up ? KeyCode.W : KeyCode.S))
                return true;

            float value = UnityEngine.Input.GetAxisRaw("Vertical");
            if (key == GameKey.Up ? value > 0.5f : value < -0.5f)
                return true;
        }
        catch (Exception)
        {
            // the legacy input backend is off on this platform
        }

        try
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                var arrow = key == GameKey.Up ? keyboard.upArrowKey : keyboard.downArrowKey;
                var letter = key == GameKey.Up ? keyboard.wKey : keyboard.sKey;
                if (arrow.isPressed || letter.isPressed)
                    return true;
            }
        }
        catch (Exception)
        {
            // no new Input System here either
        }

        return false;
    }

    private static bool Read(GameKey key, out string source)
    {
        source = null;

        var manager = Manager();
        if (manager != null)
        {
            try
            {
                if (manager.GetKeyDown(Action(key)))
                {
                    source = "the game's input manager";
                    return true;
                }
            }
            catch (Exception e)
            {
                LogOnce("manager-read", $"[Input] the game's input read failed: {e.Message}");
            }
        }

        if (LegacyKeyDown(key))
        {
            source = "the keyboard";
            return true;
        }

        if (LegacyPadDown(key))
        {
            source = "the gamepad";
            return true;
        }

        if (AxisPressed(key))
        {
            source = "a stick or d-pad";
            return true;
        }

        if (NewInputDown(key))
        {
            source = "the new Input System";
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------ game manager
    private static Il2CppSystem.Input.InputSystemManager Manager()
    {
        if (_manager != null)
            return _manager;

        // not every scene has one (title screen, field), so retry now and then
        int frame = Time.frameCount;
        if (_lookupFailed && frame - _lookupFrame < 60)
            return null;

        _lookupFrame = frame;

        try
        {
            // the game's own accessor (SingletonMonoBehaviour<InputSystemManager>.Instance)
            _manager = Il2CppSystem.SingletonMonoBehaviour<Il2CppSystem.Input.InputSystemManager>.Instance;
        }
        catch (Exception)
        {
            _manager = null;
        }

        if (_manager == null)
        {
            try
            {
                _manager = UnityEngine.Object.FindObjectOfType<Il2CppSystem.Input.InputSystemManager>();
            }
            catch (Exception)
            {
                _manager = null;
            }
        }

        _lookupFailed = _manager == null;
        return _manager;
    }

    private static Il2CppSystem.Input.InputSystemManager.InputActionType Action(GameKey key) => key switch
    {
        GameKey.Up => Il2CppSystem.Input.InputSystemManager.InputActionType.Up,
        GameKey.Down => Il2CppSystem.Input.InputSystemManager.InputActionType.Down,
        GameKey.Left => Il2CppSystem.Input.InputSystemManager.InputActionType.Left,
        GameKey.Right => Il2CppSystem.Input.InputSystemManager.InputActionType.Right,
        GameKey.Confirm => Il2CppSystem.Input.InputSystemManager.InputActionType.Enter,
        GameKey.Cancel => Il2CppSystem.Input.InputSystemManager.InputActionType.Cancel,
        _ => Il2CppSystem.Input.InputSystemManager.InputActionType.None
    };

    // ---------------------------------------------------------------- keyboard
    private static bool LegacyKeyDown(GameKey key)
    {
        if (_legacyUnavailable)
            return false;

        try
        {
            return UnityEngine.Input.GetKeyDown(LegacyKey(key));
        }
        catch (Exception)
        {
            _legacyUnavailable = true;
            return false;
        }
    }

    private static KeyCode LegacyKey(GameKey key) => key switch
    {
        GameKey.Up => KeyCode.UpArrow,
        GameKey.Down => KeyCode.DownArrow,
        GameKey.Left => KeyCode.LeftArrow,
        GameKey.Right => KeyCode.RightArrow,
        GameKey.Confirm => KeyCode.Return,
        GameKey.Cancel => KeyCode.Escape,
        _ => KeyCode.None
    };

    /// <summary>XInput face buttons: A confirms, B cancels.</summary>
    private static bool LegacyPadDown(GameKey key)
    {
        if (_legacyUnavailable)
            return false;

        try
        {
            return key switch
            {
                GameKey.Confirm => UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton0),
                GameKey.Cancel => UnityEngine.Input.GetKeyDown(KeyCode.JoystickButton1),
                _ => false
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Turns a held direction on the stick or d-pad into a single event, so the helper steps
    /// one row per push instead of racing through the list.
    /// </summary>
    private static bool AxisPressed(GameKey key)
    {
        string axis;
        bool positive;
        switch (key)
        {
            case GameKey.Up:
                axis = "Vertical";
                positive = true;
                break;
            case GameKey.Down:
                axis = "Vertical";
                positive = false;
                break;
            case GameKey.Left:
                axis = "Horizontal";
                positive = false;
                break;
            case GameKey.Right:
                axis = "Horizontal";
                positive = true;
                break;
            default:
                return false;
        }

        if (_legacyUnavailable || _axesUnavailable)
            return false;

        float value;
        try
        {
            value = UnityEngine.Input.GetAxisRaw(axis);
        }
        catch (Exception)
        {
            _axesUnavailable = true;
            return false;
        }

        float direction = value > 0.5f ? 1f : value < -0.5f ? -1f : 0f;
        AxisEdge.TryGetValue(key, out float previous);
        AxisEdge[key] = direction;

        bool wanted = positive ? direction > 0f : direction < 0f;
        bool wasWanted = positive ? previous > 0f : previous < 0f;

        return wanted && !wasWanted;
    }

    // ------------------------------------------------------------ new input system
    private static bool NewInputDown(GameKey key)
    {
        try
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard == null)
                return false;

            return key switch
            {
                GameKey.Up => keyboard.upArrowKey.wasPressedThisFrame
                              || keyboard.wKey.wasPressedThisFrame,
                GameKey.Down => keyboard.downArrowKey.wasPressedThisFrame
                                || keyboard.sKey.wasPressedThisFrame,
                GameKey.Left => keyboard.leftArrowKey.wasPressedThisFrame
                                || keyboard.aKey.wasPressedThisFrame,
                GameKey.Right => keyboard.rightArrowKey.wasPressedThisFrame
                                 || keyboard.dKey.wasPressedThisFrame,
                GameKey.Confirm => keyboard.enterKey.wasPressedThisFrame
                                   || keyboard.spaceKey.wasPressedThisFrame
                                   || keyboard.zKey.wasPressedThisFrame,
                GameKey.Cancel => keyboard.escapeKey.wasPressedThisFrame
                                  || keyboard.backspaceKey.wasPressedThisFrame
                                  || keyboard.xKey.wasPressedThisFrame,
                _ => false
            };
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly HashSet<string> Logged = new();

    private static void LogOnce(string key, string message)
    {
        if (Logged.Add(key))
            Plugin.LogSource?.LogWarning(message);
    }

    /// <summary>One line for the log: whether the game's own manager could be reached.</summary>
    public static string Describe()
    {
        bool manager;
        try
        {
            manager = Manager() != null;
        }
        catch
        {
            manager = false;
        }

        return $"gameInputManager={(manager ? "yes" : "no")}";
    }
}
