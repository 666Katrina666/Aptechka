namespace Aptechka.App;

public partial class App : Microsoft.Maui.Controls.Application
{
    private readonly MainPage mainPage;

    public App(MainPage mainPage)
    {
        InitializeComponent();
        this.mainPage = mainPage;
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(mainPage);
}
