namespace Jobly.UI;

/// <summary>
/// Implement this interface to validate credentials for the built-in Jobly login page.
/// When set on JoblyUIOptions.CredentialValidator, Jobly serves a login form and manages
/// an HTTP-only authentication cookie automatically.
/// </summary>
public interface IJoblyCredentialValidator
{
    bool Validate(string username, string password);
}
