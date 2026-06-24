using Avalonia.Input;

namespace Hourglass.Linux.Avalonia;

internal enum WindowShortcutAction
{
    None,
    PauseResume,
    Stop,
    Restart,
    Escape,
    ToggleFullScreen
}

internal static class WindowShortcutRouter
{
    public static WindowShortcutAction Resolve(Key key, KeyModifiers modifiers, bool isEditableTextFocused)
    {
        if (modifiers == KeyModifiers.None)
        {
            return key switch
            {
                Key.Space when !isEditableTextFocused => WindowShortcutAction.PauseResume,
                Key.Escape => WindowShortcutAction.Escape,
                _ => WindowShortcutAction.None
            };
        }

        if (modifiers == KeyModifiers.Control)
        {
            return key switch
            {
                Key.P => WindowShortcutAction.PauseResume,
                Key.S => WindowShortcutAction.Stop,
                Key.R => WindowShortcutAction.Restart,
                _ => WindowShortcutAction.None
            };
        }

        return modifiers == KeyModifiers.Alt && key == Key.Enter
            ? WindowShortcutAction.ToggleFullScreen
            : WindowShortcutAction.None;
    }
}
