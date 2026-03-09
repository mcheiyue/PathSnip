using System;
using System.Windows.Input;

namespace PathSnip.Services.Overlay
{
    public enum OverlayShortcutAction
    {
        None,
        Cancel,
        Pin,
        CopyColor,
        CycleNext,
        CyclePrevious,
        BypassOn,
        BypassOff
    }

    public sealed class OverlayShortcutHandler
    {
        public OverlayShortcutAction ResolveKeyDown(Key key, Key imeProcessedKey, bool canPin, bool canCopyColor, bool canCycle, bool canBypass)
        {
            if (key == Key.Escape)
            {
                return OverlayShortcutAction.Cancel;
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
            return key == Key.C || (key == Key.ImeProcessed && imeProcessedKey == Key.C);
        }

        private static bool IsPinShortcutKey(Key key, Key imeProcessedKey)
        {
            return key == Key.T || (key == Key.ImeProcessed && imeProcessedKey == Key.T);
        }

        private static bool IsBypassKey(Key key)
        {
            return key == Key.LeftAlt || key == Key.RightAlt || key == Key.System;
        }
    }
}
