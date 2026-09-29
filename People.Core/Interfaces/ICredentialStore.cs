namespace People.Core.Interfaces;

public interface ICredentialStore
{
    void SaveCredential(string resource, string username, string password);
    string? GetCredential(string resource, string username);
    void RemoveCredential(string resource, string username);
}
