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

    [ObservableProperty]
    private string? _phoneNumber;

    [ObservableProperty]
    private string? _profilePictureUrl;
}
