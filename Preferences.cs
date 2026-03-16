using System;
using System.IO;
using System.Text.Json;

namespace HintOverlay
{
    internal sealed class Preferences
    {
        public bool ShowRectangles { get; set; } = false;
        public int HotkeyModifiers { get; set; } = 0x0003; // MOD_CONTROL | MOD_ALT
        public int HotkeyVirtualKey { get; set; } = 0x48; // H key

        private static readonly string PrefsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HintOverlay",
            "preferences.json");

        public static Preferences Load()
        {
            try
            {
                if (File.Exists(PrefsPath))
                {
                    var json = File.ReadAllText(PrefsPath);
                    return JsonSerializer.Deserialize<Preferences>(json) ?? new Preferences();
                }
            }
            catch { }

            return new Preferences();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(PrefsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PrefsPath, json);
            }
            catch { }
        }
    }
}