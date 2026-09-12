using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Support.UI;

namespace Aptechka.Android.E2E.Tests;

public sealed class AndroidSession : IAsyncLifetime, IDisposable
{
    public AndroidDriver? Driver { get; private set; }

    public async Task InitializeAsync()
    {
        if (!E2ESettings.IsConfigured)
        {
            return;
        }

        var apk = E2ESettings.ApkPath;
        if (!File.Exists(apk))
        {
            throw new FileNotFoundException($"E2E APK was not found at '{apk}'.", apk);
        }

        var options = new AppiumOptions
        {
            PlatformName = "Android",
            AutomationName = "UiAutomator2",
            App = apk,
        };
        options.AddAdditionalAppiumOption("udid", E2ESettings.DeviceId);
        options.AddAdditionalAppiumOption("appPackage", E2ESettings.E2EPackageId);
        options.AddAdditionalAppiumOption("autoGrantPermissions", true);
        options.AddAdditionalAppiumOption("noReset", true);
        options.AddAdditionalAppiumOption("newCommandTimeout", 120);

        Driver = new AndroidDriver(E2ESettings.AppiumUrl, options, TimeSpan.FromSeconds(180));
        Driver.Manage().Timeouts().ImplicitWait = TimeSpan.Zero;
        await Task.CompletedTask;
    }

    public AppiumElement WaitVisible(string automationId, TimeSpan? timeout = null)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, timeout ?? TimeSpan.FromSeconds(30));
        wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
        try
        {
            return wait.Until(current =>
            {
                var element = FindDisplayed(current, automationId);
                return element is { Displayed: true } ? element : null;
            }) ?? throw new InvalidOperationException($"Element '{automationId}' was not displayed.");
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException($"Timed out waiting for element '{automationId}'.");
        }
    }

    public void WaitGone(string automationId, TimeSpan? timeout = null)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            wait.Until(current =>
            {
                var matches = FindAll(current, automationId);
                return matches.Count == 0 || matches.All(static element => !element.Displayed);
            });
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException($"Timed out waiting for element '{automationId}' to disappear.");
        }
    }

    public void PressSystemBack()
    {
        RequireDriver().ExecuteScript(
            "mobile: pressKey",
            new Dictionary<string, object> { ["keycode"] = 4 });
    }

    public bool HasText(string text) =>
        RequireDriver().PageSource.Contains(text, StringComparison.Ordinal);

    public void Dispose() => Quit();

    public Task DisposeAsync()
    {
        Quit();
        return Task.CompletedTask;
    }

    private AndroidDriver RequireDriver() =>
        Driver ?? throw new InvalidOperationException("The Appium Android session was not started.");

    // MAUI ToolbarItems expose AutomationId as accessibility id (content-desc).
    // Layouts and labels typically expose it as Android resource-id instead.
    private static IReadOnlyList<By> LocatorsFor(string automationId) =>
    [
        MobileBy.AccessibilityId(automationId),
        MobileBy.Id(automationId),
        MobileBy.Id($"{E2ESettings.E2EPackageId}:id/{automationId}"),
    ];

    private static AppiumElement? FindDisplayed(ISearchContext current, string automationId) =>
        FindAll(current, automationId).FirstOrDefault(static element =>
        {
            try
            {
                return element.Displayed;
            }
            catch (StaleElementReferenceException)
            {
                return false;
            }
        });

    private static List<AppiumElement> FindAll(ISearchContext current, string automationId)
    {
        var matches = new List<AppiumElement>();
        foreach (var locator in LocatorsFor(automationId))
        {
            foreach (var element in current.FindElements(locator))
            {
                matches.Add((AppiumElement)element);
            }
        }

        return matches;
    }

    private void Quit()
    {
        try
        {
            Driver?.Quit();
        }
        catch
        {
        }
        finally
        {
            Driver?.Dispose();
            Driver = null;
        }
    }
}
