using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BreakersOfE.Services
{
    /// <summary>
    /// A named set of Custom colours (Settings → Appearance → Custom). Starts
    /// from Light or Dark (<see cref="Base"/>); each colour is "#RRGGBB", or
    /// "" to keep the base theme's own. The card tables never use these.
    /// </summary>
    public sealed class ColorPreset
    {
        public string Name { get; set; } = "Custom";

        /// <summary>"Dark" or "Light": the theme the preset starts from.</summary>
        public string Base { get; set; } = "Dark";

        public string Window { get; set; } = "";
        public string Panel { get; set; } = "";
        public string Text { get; set; } = "";
        public string SecondaryText { get; set; } = "";
        /// <summary>"" = Windows' accent colour.</summary>
        public string Accent { get; set; } = "";
        public string Warning { get; set; } = "";

        /// <summary>The colours a preset can change, in the order Settings lists them.</summary>
        public static readonly (string Key, string Label, string Hint)[] Slots =
        {
            ("Window", "Window background", "Behind everything (turns off the see-through Mica effect)"),
            ("Panel", "Panel background", "The boxes and sections on pages and in windows"),
            ("Text", "Text", "Normal text"),
            ("SecondaryText", "Secondary text", "Hints, status lines, smaller notes"),
            ("Accent", "Accent", "Main buttons, selections, highlights"),
            ("Warning", "Warning", "Things that need your attention (amber)"),
        };

        public string Get(string key) => key switch
        {
            "Window" => Window,
            "Panel" => Panel,
            "Text" => Text,
            "SecondaryText" => SecondaryText,
            "Accent" => Accent,
            "Warning" => Warning,
            _ => "",
        };

        public void Set(string key, string value)
        {
            value = Normalize(value);
            switch (key)
            {
                case "Window": Window = value; break;
                case "Panel": Panel = value; break;
                case "Text": Text = value; break;
                case "SecondaryText": SecondaryText = value; break;
                case "Accent": Accent = value; break;
                case "Warning": Warning = value; break;
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsLight => string.Equals(Base, "Light", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The base theme's own colour for a slot (what "" means), as
        /// "#RRGGBB". Accent: Windows' blue (the real one comes from Windows).
        /// </summary>
        public static string DefaultFor(bool light, string key) => key switch
        {
            "Window" => light ? "#FAFAFA" : "#202020",
            "Panel" => light ? "#F0F0F0" : "#282828",
            "Text" => light ? "#1B1B1B" : "#FFFFFF",
            "SecondaryText" => light ? "#5D5D5D" : "#CFCFCF",
            "Accent" => "#0078D4",
            "Warning" => light ? "#B07800" : "#E8A317",     // deeper amber reads on light backgrounds
            _ => "#808080",
        };

        public ColorPreset Copy(string name) => new()
        {
            Name = name, Base = Base, Window = Window, Panel = Panel, Text = Text,
            SecondaryText = SecondaryText, Accent = Accent, Warning = Warning,
        };

        /// <summary>"#rgb", "rrggbb", "#RRGGBB" → "#RRGGBB"; anything else → "".</summary>
        public static string Normalize(string? value)
        {
            string v = (value ?? "").Trim().TrimStart('#');
            if (v.Length == 3) v = string.Concat(v.Select(c => $"{c}{c}"));
            if (v.Length != 6 || !int.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return "";
            return "#" + v.ToUpperInvariant();
        }

        /// <summary>(r, g, b) of "#RRGGBB".</summary>
        public static (byte R, byte G, byte B) Rgb(string hex)
        {
            string v = Normalize(hex);
            if (v.Length == 0) return (0x80, 0x80, 0x80);
            int n = int.Parse(v[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return ((byte)(n >> 16), (byte)(n >> 8), (byte)n);
        }

        /// <summary>
        /// WCAG contrast ratio of two colours (1 = same … 21 = black on white).
        /// Text reads well from about 4.5, large or secondary text from 3.
        /// </summary>
        public static double Contrast(string a, string b)
        {
            static double Lum(string hex)
            {
                var (r, g, bl) = Rgb(hex);
                static double Ch(byte c)
                {
                    double s = c / 255.0;
                    return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
                }
                return 0.2126 * Ch(r) + 0.7152 * Ch(g) + 0.0722 * Ch(bl);
            }
            double la = Lum(a), lb = Lum(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        /// <summary>Clean up presets from Settings.json: valid colours, unique names, at least one.</summary>
        public static List<ColorPreset> Clean(List<ColorPreset>? presets)
        {
            var list = new List<ColorPreset>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in presets ?? new List<ColorPreset>())
            {
                if (p == null) continue;
                string name = (p.Name ?? "").Trim();
                if (name.Length == 0) name = "Custom";
                string unique = name;
                for (int i = 2; names.Contains(unique); i++) unique = $"{name} {i}";
                names.Add(unique);
                p.Name = unique;
                p.Base = p.IsLight ? "Light" : "Dark";
                foreach (var (key, _, _) in Slots) p.Set(key, p.Get(key));
                list.Add(p);
            }
            if (list.Count == 0) list.Add(new ColorPreset());
            return list;
        }
    }
}
