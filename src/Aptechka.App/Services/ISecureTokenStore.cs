namespace Aptechka.App.Services;

public interface ISecureTokenStore
{
    Task<bool> HasTokenAsync();

    Task<string?> GetTokenAsync();

    Task SaveTokenAsync(string token);
}
