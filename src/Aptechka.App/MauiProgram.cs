using Aptechka.App.ViewModels;
using Aptechka.App.Services;
using Aptechka.Application.Inventory;
using Aptechka.Application.Sync;
using Aptechka.Infrastructure.GitHub;
using Aptechka.Infrastructure.Storage;
using Aptechka.Infrastructure.Sync;
using Microsoft.Extensions.Logging;

namespace Aptechka.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IClock, SystemClock>();
        builder.Services.AddSingleton<IIdGenerator, UlidIdGenerator>();
        builder.Services.AddSingleton(services =>
            new FileInventoryRepository(
                Path.Combine(FileSystem.AppDataDirectory, "data"),
                services.GetRequiredService<IClock>(),
                services.GetRequiredService<IIdGenerator>()));
        builder.Services.AddSingleton<IInventoryRepository>(services =>
            services.GetRequiredService<FileInventoryRepository>());
        builder.Services.AddSingleton<IPackageRepository>(services =>
            services.GetRequiredService<FileInventoryRepository>());
        builder.Services.AddSingleton<IProblemRepository>(services =>
            services.GetRequiredService<FileInventoryRepository>());
        builder.Services.AddSingleton<IDataSnapshotStore>(services =>
            services.GetRequiredService<FileInventoryRepository>());
        builder.Services.AddSingleton<InventoryService>();
        builder.Services.AddSingleton<PackageService>();
        builder.Services.AddSingleton<ProblemService>();
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/"),
            Timeout = TimeSpan.FromSeconds(30),
        });
        builder.Services.AddSingleton<IGitHubDataClient, GitHubDataClient>();
        builder.Services.AddSingleton(services => new SyncStateStore(
            Path.Combine(FileSystem.AppDataDirectory, "sync", "state.json"),
            services.GetRequiredService<IClock>()));
        builder.Services.AddSingleton<ISyncStateInspector, SyncStateInspector>();
        builder.Services.AddSingleton<ISyncService, GitHubSyncService>();
        builder.Services.AddSingleton<ISecureTokenStore, MauiSecureTokenStore>();
        builder.Services.AddSingleton<AutoSyncScheduler>();
        builder.Services.AddSingleton<AppSyncLifetime>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<AutoSyncCoordinator>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<ItemEditorViewModel>();
        builder.Services.AddTransient<ItemEditorPage>();
        builder.Services.AddTransient<PackageEditorViewModel>();
        builder.Services.AddTransient<PackageEditorPage>();
        builder.Services.AddTransient<ConflictResolutionViewModel>();
        builder.Services.AddTransient<ConflictResolutionPage>();

        return builder.Build();
    }
}
