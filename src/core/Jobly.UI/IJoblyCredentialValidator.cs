namespace Jobly.UI;

/// <summary>
/// Implement this interface to validate credentials for the built-in Jobly login page.
/// Register in DI as scoped. Can inject DbContext or other services for async DB lookups.
/// </summary>
public interface IJoblyCredentialValidator
{
    Task<bool> ValidateAsync(string username, string password);
}
