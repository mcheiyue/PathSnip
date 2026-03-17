using System;
using System.Windows.Input;

namespace PathSnip.Services.Overlay
{
    public enum OverlayShortcutAction
    {
        None,
        Cancel,
        Save,
        Undo,
        Pin,
        CopyColor,
        CycleNext,
        CyclePrevious,
        BypassOn,
        BypassOff
    }

    public sealed class OverlayShortcutHandler
    {
        public OverlayShortcutAction ResolveKeyDown(Key key, Key imeProcessedKey, bool canPin, bool canCopyColor, bool canCycle, bool canBypass, bool canSave, bool canUndo)
        {
            if (key == Key.Escape)
            {
                return OverlayShortcutAction.Cancel;
            }

            if (canSave && IsSaveKey(key, imeProcessedKey))
            {
                return OverlayShortcutAction.Save;
            }

            if (canUndo && IsUndoKey(key, imeProcessedKey))
            {
                return OverlayShortcutAction.Undo;
            }

            if (canBypass && IsBypassKey(key))
            {
                return OverlayShortcutAction.BypassOn;
            }

            if (canCycle && key == Key.Tab)
            {
                return (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift
                    ? OverlayShortcutAction.CyclePrevious
                    : OverlayShortcutAction.CycleNext;
            }

            if (canPin && IsPinShortcutKey(key, imeProcessedKey))
            {
                return OverlayShortcutAction.Pin;
            }

            if (canCopyColor && IsColorCopyKey(key, imeProcessedKey))
            {
                return OverlayShortcutAction.CopyColor;
            }

            return OverlayShortcutAction.None;
        }

        public OverlayShortcutAction ResolveKeyUp(Key key, bool canBypass)
        {
            if (canBypass && IsBypassKey(key))
            {
                return OverlayShortcutAction.BypassOff;
            }

            return OverlayShortcutAction.None;
        }

        public OverlayShortcutAction ResolveTextInput(string text, bool canPin, bool canCopyColor)
        {
            if (canPin && string.Equals(text, "t", StringComparison.OrdinalIgnoreCase))
            {
                return OverlayShortcutAction.Pin;
            }

            if (canCopyColor && string.Equals(text, "c", StringComparison.OrdinalIgnoreCase))
            {
                return OverlayShortcutAction.CopyColor;
            }

            return OverlayShortcutAction.None;
        }

        private static bool IsColorCopyKey(Key key, Key imeProcessedKey)
        {
            return key == Key.C || IsImeProcessedKey(key, imeProcessedKey, Key.C);
        }

        private static bool IsPinShortcutKey(Key key, Key imeProcessedKey)
        {
            return key == Key.T || IsImeProcessedKey(key, imeProcessedKey, Key.T);
        }

        private static bool IsSaveKey(Key key, Key imeProcessedKey)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return false;
            }

            return key == Key.Return
                || key == Key.Enter
                || IsImeProcessedKey(key, imeProcessedKey, Key.Return)
                || IsImeProcessedKey(key, imeProcessedKey, Key.Enter);
        }

        private static bool IsUndoKey(Key key, Key imeProcessedKey)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            if ((modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return false;
            }

            if ((modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
            {
                return false;
            }

            if ((modifiers & (ModifierKeys.Alt | ModifierKeys.Windows)) != 0)
            {
                return false;
            }

            return key == Key.Z || IsImeProcessedKey(key, imeProcessedKey, Key.Z);
        }

        private static bool IsImeProcessedKey(Key key, Key imeProcessedKey, Key expected)
        {
            return key == Key.ImeProcessed && imeProcessedKey == expected;
        }

        private static bool IsBypassKey(Key key)
        {
            return key == Key.LeftAlt || key == Key.RightAlt || key == Key.System;
        }
    }
}
