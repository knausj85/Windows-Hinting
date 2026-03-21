using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using HintOverlay.Models;

namespace HintOverlay.Services
{
    internal sealed class PreferencesService : IPreferencesService
    {
        private static readonly string PrefsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Windows-Hinting",
            "preferences.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public HintOverlayOptions Load()
        {
            try
            {
                if (File.Exists(PrefsPath))
                {
                    var json = File.ReadAllText(PrefsPath);
                    return JsonSerializer.Deserialize<HintOverlayOptions>(json, JsonOptions) ?? new HintOverlayOptions();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load preferences: {ex.Message}");
            }

            return new HintOverlayOptions();
        }

        public void Save(HintOverlayOptions options)
        {
            try
            {
                var dir = Path.GetDirectoryName(PrefsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(options, JsonOptions);
                File.WriteAllText(PrefsPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save preferences: {ex.Message}");
            }
        }
    }
}
