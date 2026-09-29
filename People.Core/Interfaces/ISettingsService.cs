namespace People.Core.Interfaces;

public interface ISettingsService
{
    T? GetValue<T>(string key, T? defaultValue = default);
    void SetValue<T>(string key, T value);
    bool ContainsKey(string key);
    void Remove(string key);
    
    event EventHandler<string>? SettingChanged;
}
