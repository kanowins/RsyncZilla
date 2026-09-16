using System;
using System.IO;
using System.Text.Json;
using RsyncZilla.Models;

namespace RsyncZilla.Services
{
    public class AppSettings
    {
        public FileExistsAction FileExistsAction { get; set; } = FileExistsAction.OverwriteIfDifferent;
    }

    public class SettingsService
    {
        private readonly string _filePath;
        private AppSettings _currentSettings;

        public AppSettings Current => _currentSettings;

        public SettingsService(string? customFilePath = null)
        {
            if (!string.IsNullOrEmpty(customFilePath))
            {
                _filePath = customFilePath;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var folder = Path.Combine(appData, "RsyncZilla");
                Directory.CreateDirectory(folder);
                _filePath = Path.Combine(folder, "settings.json");
            }

            _currentSettings = LoadSettings();
        }

        public AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        return settings;
                    }
                }
            }
            catch
            {
                // Fallback to default on any read error
            }

            return new AppSettings();
        }

        public void SaveSettings(AppSettings settings)
        {
            try
            {
                _currentSettings = settings ?? new AppSettings();
                var dir = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonSerializer.Serialize(_currentSettings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Non-fatal error during saving
            }
        }

        public void SaveFileExistsAction(FileExistsAction action)
        {
            _currentSettings.FileExistsAction = action;
            SaveSettings(_currentSettings);
        }
    }
}
