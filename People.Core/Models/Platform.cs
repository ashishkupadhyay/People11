namespace People.Core.Models;

public class Platform
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public PlatformColors Colors { get; set; } = new();
}
