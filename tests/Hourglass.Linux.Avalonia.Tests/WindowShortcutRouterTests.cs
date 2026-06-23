namespace Hourglass.Linux.Avalonia.Tests;

using global::Avalonia.Input;
using Xunit;

public sealed class WindowShortcutRouterTests
{
    [Theory]
    [InlineData(Key.P, KeyModifiers.Control, (int)WindowShortcutAction.PauseResume)]
    [InlineData(Key.S, KeyModifiers.Control, (int)WindowShortcutAction.Stop)]
    [InlineData(Key.R, KeyModifiers.Control, (int)WindowShortcutAction.Restart)]
    [InlineData(Key.Escape, KeyModifiers.None, (int)WindowShortcutAction.Escape)]
    [InlineData(Key.Enter, KeyModifiers.Alt, (int)WindowShortcutAction.ToggleFullScreen)]
    public void ResolvesGlobalShortcuts(Key key, KeyModifiers modifiers, int expected)
    {
        Assert.Equal((WindowShortcutAction)expected, WindowShortcutRouter.Resolve(key, modifiers, isEditableTextFocused: false));
    }

    [Fact]
    public void SpacePausesOutsideEditableTextOnly()
    {
        Assert.Equal(
            WindowShortcutAction.PauseResume,
            WindowShortcutRouter.Resolve(Key.Space, KeyModifiers.None, isEditableTextFocused: false));
        Assert.Equal(
            WindowShortcutAction.None,
            WindowShortcutRouter.Resolve(Key.Space, KeyModifiers.None, isEditableTextFocused: true));
    }

    [Theory]
    [InlineData(Key.P, KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(Key.Space, KeyModifiers.Control)]
    [InlineData(Key.Enter, KeyModifiers.Control | KeyModifiers.Alt)]
    public void IgnoresUnspecifiedModifierCombinations(Key key, KeyModifiers modifiers)
    {
        Assert.Equal(
            WindowShortcutAction.None,
            WindowShortcutRouter.Resolve(key, modifiers, isEditableTextFocused: false));
    }
}
