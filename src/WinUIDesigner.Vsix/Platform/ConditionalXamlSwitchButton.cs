// Copyright (c) 0x5BFA. All rights reserved.
// Licensed under MIT License.

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WinUIDesigner.Platform;

/// <summary>
/// The rules of the Conditions button that need no designer: when it is shown, which column of the device toolbar it
/// takes, and what a key does to the list.
/// </summary>
internal static class ConditionalXamlSwitchButton
{
    /// <summary>
    /// What a key pressed on the button does to the list.
    /// </summary>
    public enum KeyAction
    {
        /// <summary>
        /// The key is not for the list.
        /// </summary>
        None,

        /// <summary>
        /// The key opens the list.
        /// </summary>
        Open,

        /// <summary>
        /// The key closes the list.
        /// </summary>
        Close,
    }

    /// <summary>
    /// Returns whether the button is shown: the option is on, and the document declares a conditional namespace or the
    /// project has overrides that the user may want to reset.
    /// </summary>
    /// <param name="isEnabled">Whether the option that shows the drop-down is on.</param>
    /// <param name="switchCount">The number of questions the document asks.</param>
    /// <param name="overrideCount">The number of questions the user forces in the project.</param>
    public static bool IsShown(bool isEnabled, int switchCount, int overrideCount) => isEnabled && (switchCount != 0 || overrideCount != 0);

    /// <summary>
    /// Returns the first column of <paramref name="grid"/> that no child occupies and that lies after every
    /// occupied one; it equals the number of columns when there is no such column.
    /// </summary>
    public static int FindFreeColumn(Grid grid)
    {
        int next = 0;
        foreach (UIElement child in grid.Children)
        {
            next = Math.Max(next, Grid.GetColumn(child) + Math.Max(1, Grid.GetColumnSpan(child)));
        }

        return next;
    }

    /// <summary>
    /// Returns what <paramref name="key"/> does to the list when the button has the keyboard focus.
    /// </summary>
    /// <param name="key">The key; the key of an Alt combination, such as Alt+Down, not <see cref="Key.System"/>.</param>
    /// <param name="isOpen">Whether the list is open.</param>
    public static KeyAction GetKeyAction(Key key, bool isOpen) => (key, isOpen) switch
    {
        // Down and F4 open the list, as they open the list of a combo box.
        (Key.Down or Key.F4, false) => KeyAction.Open,

        // The focus stays on the button when no control of the list can take it, so Escape reaches the button, not the list.
        (Key.Escape, true) => KeyAction.Close,

        // Any other key, and Down or F4 on an open list or Escape on a closed one, is not for the list.
        _ => KeyAction.None,
    };
}
