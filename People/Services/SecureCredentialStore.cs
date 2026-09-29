using People.Core.Interfaces;
using Windows.Security.Credentials;

namespace People.Services;

public class SecureCredentialStore : ICredentialStore
{
    private readonly PasswordVault _vault = new PasswordVault();

    public void SaveCredential(string resource, string username, string password)
    {
        var credential = new PasswordCredential(resource, username, password);
        _vault.Add(credential);
    }

    public string? GetCredential(string resource, string username)
    {
        try
        {
            var credential = _vault.Retrieve(resource, username);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070490) // Element not found
        {
            return null;
        }
    }

    public void RemoveCredential(string resource, string username)
    {
        try
        {
            var credential = _vault.Retrieve(resource, username);
            _vault.Remove(credential);
        }
        catch (Exception ex) when ((uint)ex.HResult == 0x80070490) // Element not found
        {
            // Already removed
        }
    }
}
