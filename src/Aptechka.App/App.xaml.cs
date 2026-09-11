using Aptechka.App.Services;

namespace Aptechka.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly IServiceProvider services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        this.services = services;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new NavigationPage(services.GetRequiredService<MainPage>()));
        services.GetRequiredService<AutoSyncCoordinator>().Attach(window);
        return window;
    }
}
