using People.Core.Interfaces;
using System.Text.Json;

namespace People.Services;

public class SettingsService : ISettingsService
{
    private readonly string _settingsFilePath;
    private Dictionary<string, object> _settings;
    
    public event EventHandler<string>? SettingChanged;

    public SettingsService()
    {
        var localFolder = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
        _settingsFilePath = Path.Combine(localFolder, "settings.json");
        _settings = new Dictionary<string, object>();
        LoadSettings();
    }

    private void LoadSettings()
    {
        if (File.Exists(_settingsFilePath))
        {
            try
            {
                var json = File.ReadAllText(_settingsFilePath);
                _settings = JsonSerializer.Deserialize<Dictionary<string, object>>(json) 
                            ?? new Dictionary<string, object>();
            }
            catch
            {
                _settings = new Dictionary<string, object>();
            }
        }
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFilePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public T? GetValue<T>(string key, T? defaultValue = default)
    {
        if (_settings.TryGetValue(key, out var value))
        {
            if (value is JsonElement element)
            {
                try
                {
                    var typedValue = element.Deserialize<T>();
                    if (typedValue != null)
                    {
                        _settings[key] = typedValue;
                        return typedValue;
                    }
                }
                catch
                {
                    return defaultValue;
                }
            }
            else if (value is T typedValue)
            {
                return typedValue;
            }
        }
        return defaultValue;
    }

    public void SetValue<T>(string key, T value)
    {
        if (value == null)
        {
            Remove(key);
            return;
        }

        bool hasChanged = false;
        if (_settings.TryGetValue(key, out var existingValue))
        {
            if (existingValue is JsonElement element)
            {
                var deserialized = element.Deserialize<T>();
                if (!EqualityComparer<T>.Default.Equals(deserialized, value))
                {
                    _settings[key] = value;
                    hasChanged = true;
                }
                else
                {
                    // Upgrade stored element to typed instance anyway
                    _settings[key] = value;
                }
            }
            else if (existingValue is T typedExisting)
            {
                if (!EqualityComparer<T>.Default.Equals(typedExisting, value))
                {
                    _settings[key] = value;
                    hasChanged = true;
                }
            }
            else
            {
                _settings[key] = value;
                hasChanged = true;
            }
        }
        else
        {
            _settings[key] = value;
            hasChanged = true;
        }

        if (hasChanged)
        {
            SaveSettings();
            SettingChanged?.Invoke(this, key);
        }
    }

    public bool ContainsKey(string key)
    {
        return _settings.ContainsKey(key);
    }

    public void Remove(string key)
    {
        if (_settings.Remove(key))
        {
            SaveSettings();
            SettingChanged?.Invoke(this, key);
        }
    }
}
