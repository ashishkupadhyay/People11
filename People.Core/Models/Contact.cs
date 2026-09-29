namespace People.Core.Models;

public class Contact
{
    public string Id { get; set; } = string.Empty;
    public string PlatformId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? ProfilePictureUrl { get; set; }
}
