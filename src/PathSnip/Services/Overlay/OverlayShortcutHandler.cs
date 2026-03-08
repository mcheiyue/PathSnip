using System;
using System.Windows.Input;

namespace PathSnip.Services.Overlay
{
    public enum OverlayShortcutAction
    {
        None,
        Cancel,
        Pin,
        CopyColor
    }

    public sealed class OverlayShortcutHandler
    {
        public OverlayShortcutAction ResolveKeyDown(Key key, Key imeProcessedKey, bool canPin, bool canCopyColor)
        {
            if (key == Key.Escape)
            {
                return OverlayShortcutAction.Cancel;
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
    }
}
