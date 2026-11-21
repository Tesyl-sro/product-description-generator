using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProductDescGen.Data;

public sealed record AppSettings(
    int ProviderIndex = 0,
    string? ApiKey = null,
    string? PreferredModel = null,
    string? CustomSystemPrompt = null,
    int ReasoningLevelIndex = 0);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal partial class AppSettingsContext : JsonSerializerContext;

public static class SettingsStore
{
    // Windows: %APPDATA%\ProductDescGen
    // macOS: ~/Library/Application Support/ProductDescGen
    // Linux: ~/.config/ProductDescGen
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ProductDescGen", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(stream, AppSettingsContext.Default.AppSettings) ?? new();
        }
        catch (Exception)
        {
            // Missing or corrupt file: start with defaults
            return new();
        }
    }
    
    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, AppSettingsContext.Default.AppSettings));

        // Keep the key readable only by the current user on macOS/Linux
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    public static void Delete()
    {
        if (File.Exists(FilePath))
            File.Delete(FilePath);
    }
}