namespace Launchly.Core;

/// <summary>Ayrıştırılmış kısayol: Win32 <c>RegisterHotKey</c> değiştirici bayrakları ve sanal tuş kodu.</summary>
public readonly record struct HotkeyDefinition(uint Modifiers, uint VirtualKey);

/// <summary>"Alt+Space", "Ctrl+Shift+K" gibi metni Win32 değerlerine çevirir.</summary>
public static class HotkeyParser
{
    public const uint ModAlt = 0x0001;
    public const uint ModControl = 0x0002;
    public const uint ModShift = 0x0004;
    public const uint ModWin = 0x0008;

    public static bool TryParse(string? text, out HotkeyDefinition result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        uint modifiers = 0;
        uint key = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0)
            {
                return false;
            }

            switch (raw.ToLowerInvariant())
            {
                case "alt": modifiers |= ModAlt; continue;
                case "ctrl" or "control": modifiers |= ModControl; continue;
                case "shift": modifiers |= ModShift; continue;
                case "win" or "windows": modifiers |= ModWin; continue;
            }

            if (key != 0 || !TryParseKey(raw, out key))
            {
                return false;
            }
        }

        // Düzenleyicisiz tek tuş (ör. "A") global kısayol olarak kabul edilmez.
        if (key == 0 || modifiers == 0)
        {
            return false;
        }

        result = new HotkeyDefinition(modifiers, key);
        return true;
    }

    private static bool TryParseKey(string name, out uint vk)
    {
        vk = 0;
        if (name.Length == 1)
        {
            var c = char.ToUpperInvariant(name[0]);
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                vk = c;
                return true;
            }

            return false;
        }

        var lower = name.ToLowerInvariant();
        if (lower.Length is 2 or 3 && lower[0] == 'f' && int.TryParse(lower.AsSpan(1), out var f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            return true;
        }

        vk = lower switch
        {
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "esc" or "escape" => 0x1B,
            "backspace" => 0x08,
            "insert" => 0x2D,
            "delete" => 0x2E,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" => 0x21,
            "pagedown" => 0x22,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            _ => 0,
        };
        return vk != 0;
    }
}
