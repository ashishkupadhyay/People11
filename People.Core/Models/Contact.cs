using CommunityToolkit.Mvvm.ComponentModel;

namespace People.Core.Models;

public partial class Contact : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _platformId = string.Empty;

    [ObservableProperty]
    private string _name = string.Empty;

    public string Initials => Chat.ExtractInitials(Name);

    partial void OnNameChanged(string value)
    {
        OnPropertyChanged(nameof(Initials));
    }

    [ObservableProperty]
    private string? _phoneNumber;

    [ObservableProperty]
    private string? _profilePictureUrl;
}
