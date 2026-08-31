namespace Aptechka.App.Services;

public sealed class MauiSecureTokenStore : ISecureTokenStore
{
    private const string GitHubTokenKey = "github-data-repository-token";

    public async Task<bool> HasTokenAsync() =>
        !string.IsNullOrWhiteSpace(await GetTokenAsync());

    public async Task<string?> GetTokenAsync() =>
        await SecureStorage.Default.GetAsync(GitHubTokenKey);

    public async Task SaveTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Нельзя сохранить пустой GitHub-токен.", nameof(token));
        }

        await SecureStorage.Default.SetAsync(GitHubTokenKey, token.Trim());
    }
}
