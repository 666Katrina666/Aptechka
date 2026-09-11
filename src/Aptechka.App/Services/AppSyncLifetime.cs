namespace Aptechka.App.Services;

public sealed class AppSyncLifetime
{
    private readonly CancellationTokenSource cancellation = new();

    public CancellationToken Token => cancellation.Token;

    public void Cancel() => cancellation.Cancel();
}
