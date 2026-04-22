using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WindowsHinting.Logging;
using WindowsHinting.Models;

namespace WindowsHinting.Services
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
            PropertyNameCaseInsensitive = true,
            Converters =
            {
                new JsonStringEnumConverter(),
                // Fault-tolerant for LogLevel so a hand-edited typo in
                // preferences.json doesn't wipe every other preference.
                new TolerantLogLevelConverter(),
            }
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

    /// <summary>
    /// Case-insensitive <see cref="LogLevel"/> converter that falls back to
    /// <see cref="LogLevel.Info"/> when the JSON contains an unrecognized string,
    /// rather than throwing and discarding every other preference.
    /// Accepts integer values too.
    /// </summary>
    internal sealed class TolerantLogLevelConverter : JsonConverter<LogLevel>
    {
        public override LogLevel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var n))
            {
                return Enum.IsDefined(typeof(LogLevel), n) ? (LogLevel)n : LogLevel.Info;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var s = reader.GetString();
                if (!string.IsNullOrWhiteSpace(s) &&
                    Enum.TryParse<LogLevel>(s, ignoreCase: true, out var parsed))
                {
                    return parsed;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"Unrecognized LogLevel '{s}' in preferences.json; defaulting to Info.");
                return LogLevel.Info;
            }

            return LogLevel.Info;
        }

        public override void Write(Utf8JsonWriter writer, LogLevel value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
