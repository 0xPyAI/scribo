using System;
using System.IO;
using System.Text.Json;
using System.Windows.Input;

namespace Scribo;

public class AppSettings
{
    private static string? _cachedSettingsPath;

    public static string SettingsFilePath
    {
        get
        {
            if (_cachedSettingsPath != null) return _cachedSettingsPath;

            // 1. Portable Mode: Check if settings.json already exists in application directory and is writable
            try
            {
                string portablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                if (File.Exists(portablePath))
                {
                    using var testStream = File.Open(portablePath, FileMode.Open, FileAccess.ReadWrite);
                    _cachedSettingsPath = portablePath;
                    return _cachedSettingsPath;
                }
            }
            catch { }

            // 2. Installed Mode: %AppData%\Scribo\settings.json
            try
            {
                string appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Scribo");

                if (!Directory.Exists(appDataDir))
                {
                    Directory.CreateDirectory(appDataDir);
                }

                _cachedSettingsPath = Path.Combine(appDataDir, "settings.json");
                return _cachedSettingsPath;
            }
            catch
            {
                // Fallback to local base directory if AppData is unavailable
                _cachedSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
                return _cachedSettingsPath;
            }
        }
    }

    // Global & System Hotkeys
    public string RecallHotkey { get; set; } = "Alt + S";
    public string HoldKey { get; set; } = "Alt";
    public bool IsVerticalOrientation { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool ShowNotification { get; set; } = true;

    // Presentation Aids
    public bool KeystrokesEnabled { get; set; } = false;
    public bool CursorHaloEnabled { get; set; } = false;
    public bool VanishingLaserEnabled { get; set; } = true;

    // Tool & Action Hotkeys (Serialized as clean key names)
    public string KeySelect { get; set; } = "S";
    public string KeyPen { get; set; } = "P";
    public string KeyHighlighter { get; set; } = "H";
    public string KeyLaser { get; set; } = "L";
    public string KeyText { get; set; } = "T";
    public string KeyStepBadge { get; set; } = "N";
    public string KeyShapes { get; set; } = "R";
    public string KeyEraser { get; set; } = "E";
    public string KeyBoard { get; set; } = "B";
    public string KeySpotlight { get; set; } = "F";
    public string KeyMagnifier { get; set; } = "M";
    public string KeyToggleInk { get; set; } = "V";
    public string KeyClear { get; set; } = "K";
    public string KeyScreenshot { get; set; } = "C";

    public static AppSettings Load()
    {
        try
        {
            string path = SettingsFilePath;
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string path = SettingsFilePath;
            string dir = Path.GetDirectoryName(path)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            
            // Atomic file write using temporary swap to prevent file corruption
            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, json);
            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        catch { }
    }

    public static Key ParseKey(string? keyStr, Key defaultKey)
    {
        if (!string.IsNullOrEmpty(keyStr) && Enum.TryParse<Key>(keyStr, true, out var key))
            return key;
        return defaultKey;
    }
}
