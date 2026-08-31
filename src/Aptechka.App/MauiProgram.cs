using Aptechka.App.ViewModels;
using Aptechka.Application.Inventory;
using Aptechka.Infrastructure.Storage;
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
        builder.Services.AddSingleton<IInventoryRepository>(services =>
            new FileInventoryRepository(
                Path.Combine(FileSystem.AppDataDirectory, "data"),
                services.GetRequiredService<IClock>(),
                services.GetRequiredService<IIdGenerator>()));
        builder.Services.AddSingleton<InventoryService>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
