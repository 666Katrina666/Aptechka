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

    public AppiumElement WaitVisible(string automationId, TimeSpan? timeout = null) =>
        WaitVisible(automationId, automationId, timeout);

    public AppiumElement WaitVisible(string automationId, string stage, TimeSpan? timeout = null)
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
            }) ?? throw new InvalidOperationException(
                $"Element '{automationId}' was not displayed during '{stage}'.");
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException(
                $"Timed out waiting for element '{automationId}' during '{stage}'.");
        }
    }

    public void WaitGone(string automationId, TimeSpan? timeout = null) =>
        WaitGone(automationId, automationId, timeout);

    public void WaitGone(string automationId, string stage, TimeSpan? timeout = null)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, timeout ?? TimeSpan.FromSeconds(15));
        try
        {
            wait.Until(current =>
            {
                var matches = FindAll(current, automationId);
                return matches.Count == 0 || matches.All(static element => !IsDisplayed(element));
            });
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException(
                $"Timed out waiting for element '{automationId}' to disappear during '{stage}'.");
        }
    }

    public AppiumElement WaitSingleVisible(string automationId, string stage, TimeSpan? timeout = null)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, timeout ?? TimeSpan.FromSeconds(30));
        wait.IgnoreExceptionTypes(typeof(NoSuchElementException), typeof(StaleElementReferenceException));
        var lastCount = 0;
        try
        {
            return wait.Until(current =>
            {
                var shown = FindAll(current, automationId).Where(IsDisplayed).ToList();
                lastCount = shown.Count;
                if (shown.Count > 1)
                {
                    throw new InvalidOperationException(
                        $"Expected one visible '{automationId}' during '{stage}', found {shown.Count}.");
                }

                return shown.Count == 1 ? shown[0] : null;
            }) ?? throw new InvalidOperationException(
                $"Expected one visible '{automationId}' during '{stage}', found 0.");
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException(
                $"Expected one visible '{automationId}' during '{stage}', found {lastCount}.");
        }
    }

    public AppiumElement ScrollTo(string automationId, string stage)
    {
        var driver = RequireDriver();
        var resourceId = $"{E2ESettings.E2EPackageId}:id/{automationId}";
        try
        {
            var byResource = MobileBy.AndroidUIAutomator(
                "new UiScrollable(new UiSelector().scrollable(true)).scrollIntoView(" +
                $"new UiSelector().resourceId(\"{resourceId}\"))");
            var scrolled = (AppiumElement)driver.FindElement(byResource);
            if (IsDisplayed(scrolled))
            {
                return scrolled;
            }
        }
        catch (NoSuchElementException)
        {
        }

        try
        {
            var byDescription = MobileBy.AndroidUIAutomator(
                "new UiScrollable(new UiSelector().scrollable(true)).scrollIntoView(" +
                $"new UiSelector().description(\"{automationId}\"))");
            var scrolled = (AppiumElement)driver.FindElement(byDescription);
            if (IsDisplayed(scrolled))
            {
                return scrolled;
            }
        }
        catch (NoSuchElementException)
        {
        }

        return WaitVisible(automationId, stage);
    }

    public void TypeInto(string automationId, string value, string stage)
    {
        var field = ResolveEditable(WaitVisible(automationId, stage), stage);
        field.Click();
        field.Clear();
        if (!string.IsNullOrEmpty(field.Text))
        {
            field.Clear();
        }

        if (value.Length > 0)
        {
            field.SendKeys(value);
        }

        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        try
        {
            wait.Until(_ => (field.Text ?? string.Empty) == value);
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException(
                $"Field '{automationId}' during '{stage}' kept '{field.Text}', expected '{value}'.");
        }

        HideKeyboardQuietly();
    }

    public void ClearField(string automationId, string stage)
    {
        var root = WaitVisible(automationId, stage);
        var close = AndroidIdCandidates("android:id/search_close_btn")
            .SelectMany(id => root.FindElements(MobileBy.Id(id)).OfType<AppiumElement>())
            .FirstOrDefault(IsDisplayed);
        if (close is not null)
        {
            close.Click();
            HideKeyboardQuietly();
            return;
        }

        var field = ResolveEditable(root, stage);
        field.Click();
        try
        {
            RequireDriver().ExecuteScript(
                "mobile: replaceElementValue",
                new Dictionary<string, object>
                {
                    ["elementId"] = field.Id,
                    ["text"] = string.Empty,
                });
        }
        catch (Exception)
        {
            field.Clear();
        }

        HideKeyboardQuietly();
    }

    public bool IsChecked(string automationId, string stage)
    {
        var element = ScrollTo(automationId, stage);
        var value = element.GetAttribute("checked");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    public IReadOnlyList<string> VisibleTexts(AppiumElement root)
    {
        return root.FindElements(By.ClassName("android.widget.TextView"))
            .Where(IsDisplayed)
            .Select(element => element.Text)
            .Where(static text => !string.IsNullOrEmpty(text))
            .ToArray();
    }

    public void AssertRowContains(AppiumElement row, string stage, params string[] expected)
    {
        var texts = VisibleTexts(row);
        foreach (var value in expected)
        {
            if (!texts.Any(text => text.Contains(value, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Row during '{stage}' did not contain '{value}'. Visible: [{string.Join(" | ", texts)}].");
            }
        }
    }

    public void ChooseActionSheetItem(
        string[] expectedItems,
        string item,
        string stage,
        string? expectedTitle = null)
    {
        WaitSystemDialog(stage);
        if (expectedTitle is not null)
        {
            var title = WaitAndroidId("android:id/alertTitle", stage + " title");
            if (title.Text != expectedTitle)
            {
                throw new InvalidOperationException(
                    $"Dialog title during '{stage}' was '{title.Text}', expected '{expectedTitle}'.");
            }
        }

        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        List<AppiumElement> items;
        try
        {
            items = wait.Until(_ =>
            {
                var found = driver.FindElements(MobileBy.Id("android:id/text1"))
                    .OfType<AppiumElement>()
                    .Where(IsDisplayed)
                    .ToList();
                return found.Count == expectedItems.Length ? found : null;
            }) ?? [];
        }
        catch (WebDriverTimeoutException)
        {
            var found = driver.FindElements(MobileBy.Id("android:id/text1"))
                .OfType<AppiumElement>()
                .Where(IsDisplayed)
                .Select(element => element.Text)
                .ToArray();
            throw new InvalidOperationException(
                $"Action sheet during '{stage}' had {found.Length} items [{string.Join(", ", found)}], expected {expectedItems.Length}.");
        }

        var texts = items.Select(element => element.Text).ToArray();
        if (!texts.SequenceEqual(expectedItems, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Action sheet during '{stage}' had [{string.Join(", ", texts)}], expected [{string.Join(", ", expectedItems)}].");
        }

        var matches = items
            .Select((element, index) => (element, text: texts[index]))
            .Where(candidate => candidate.text == item)
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(
                $"Action sheet during '{stage}' matched '{item}' {matches.Count} times.");
        }

        matches[0].element.Click();
        WaitActionSheetGone(stage);
    }

    public void SubmitPrompt(string value, string stage, string expectedTitle, string expectedPositive)
    {
        var dialog = WaitSystemDialog(stage);
        var title = WaitAndroidId("android:id/alertTitle", stage + " title");
        if (title.Text != expectedTitle)
        {
            throw new InvalidOperationException(
                $"Prompt title during '{stage}' was '{title.Text}', expected '{expectedTitle}'.");
        }

        var field = FindEditIn(dialog, stage);
        field.Click();
        field.Clear();
        field.SendKeys(value);
        var positive = WaitAndroidId("android:id/button1", stage + " confirm");
        if (positive.Text != expectedPositive)
        {
            throw new InvalidOperationException(
                $"Prompt confirm during '{stage}' was '{positive.Text}', expected '{expectedPositive}'.");
        }

        positive.Click();
        WaitSystemDialogGone(stage);
    }

    public void PressSystemBack()
    {
        RequireDriver().ExecuteScript(
            "mobile: pressKey",
            new Dictionary<string, object> { ["keycode"] = 4 });
    }

    public bool HasText(string text) =>
        RequireDriver().PageSource.Contains(text, StringComparison.Ordinal);

    public void SavePageSource(string stage)
    {
        try
        {
            var driver = Driver;
            if (driver is null)
            {
                return;
            }

            Directory.CreateDirectory(E2ESettings.ArtifactDir);
            var safe = string.Join("_", stage.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrWhiteSpace(safe))
            {
                safe = "unknown";
            }

            var path = Path.Combine(
                E2ESettings.ArtifactDir,
                $"pagesource-{safe}-{DateTime.UtcNow:yyyyMMddHHmmss}.xml");
            File.WriteAllText(path, driver.PageSource);
        }
        catch
        {
        }
    }

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
        FindAll(current, automationId).FirstOrDefault(IsDisplayed);

    private static List<AppiumElement> FindAll(ISearchContext current, string automationId)
    {
        var matches = new List<AppiumElement>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var locator in LocatorsFor(automationId))
        {
            foreach (var element in current.FindElements(locator))
            {
                var appium = (AppiumElement)element;
                if (seen.Add(appium.Id))
                {
                    matches.Add(appium);
                }
            }
        }

        return matches;
    }

    private static bool IsDisplayed(IWebElement element)
    {
        try
        {
            return element.Displayed;
        }
        catch (StaleElementReferenceException)
        {
            return false;
        }
    }

    private AppiumElement ResolveEditable(AppiumElement root, string stage)
    {
        if (IsEditable(root))
        {
            return root;
        }

        var inner = root.FindElements(MobileBy.Id("android:id/search_src_text"))
            .Concat(root.FindElements(By.ClassName("android.widget.EditText")))
            .Concat(root.FindElements(By.ClassName("android.widget.AutoCompleteTextView")))
            .OfType<AppiumElement>()
            .FirstOrDefault(IsDisplayed);
        if (inner is not null)
        {
            return inner;
        }

        throw new InvalidOperationException($"No editable field under the element during '{stage}'.");
    }

    private static bool IsEditable(AppiumElement element)
    {
        var className = element.GetAttribute("class") ?? element.TagName ?? string.Empty;
        return className.Contains("EditText", StringComparison.OrdinalIgnoreCase)
            || className.Contains("AutoCompleteTextView", StringComparison.OrdinalIgnoreCase);
    }

    private AppiumElement WaitSystemDialog(string stage)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        try
        {
            return wait.Until(_ =>
                FindDisplayedByAndroidId("android:id/parentPanel")
                ?? FindDisplayedByAndroidId("android:id/contentPanel")
                ?? FindDisplayedByAndroidId("android:id/alertTitle"))
                ?? throw new InvalidOperationException($"Android dialog was not shown during '{stage}'.");
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException($"Timed out waiting for Android dialog during '{stage}'.");
        }
    }

    private void WaitActionSheetGone(string stage)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        try
        {
            wait.Until(_ =>
                FindDisplayedByAndroidId("select_dialog_listview") is null
                && !driver.FindElements(MobileBy.Id("android:id/text1")).Any(IsDisplayed));
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException($"Action sheet stayed open after '{stage}'.");
        }
    }

    private void WaitSystemDialogGone(string stage)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
        try
        {
            wait.Until(_ =>
                FindDisplayedByAndroidId("android:id/parentPanel") is null
                && FindDisplayedByAndroidId("android:id/alertTitle") is null
                && !driver.FindElements(MobileBy.Id("android:id/text1")).Any(IsDisplayed));
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException($"Android dialog stayed open after '{stage}'.");
        }
    }

    private AppiumElement WaitAndroidId(string id, string stage)
    {
        var driver = RequireDriver();
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(10));
        try
        {
            return wait.Until(_ => FindDisplayedByAndroidId(id))
                ?? throw new InvalidOperationException($"Android id '{id}' was not shown during '{stage}'.");
        }
        catch (WebDriverTimeoutException)
        {
            throw new InvalidOperationException($"Timed out waiting for Android id '{id}' during '{stage}'.");
        }
    }

    private AppiumElement? FindDisplayedByAndroidId(string id)
    {
        foreach (var candidate in AndroidIdCandidates(id))
        {
            var found = RequireDriver()
                .FindElements(MobileBy.Id(candidate))
                .OfType<AppiumElement>()
                .FirstOrDefault(IsDisplayed);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static IEnumerable<string> AndroidIdCandidates(string id)
    {
        yield return id;
        var name = id.Contains('/', StringComparison.Ordinal) ? id[(id.LastIndexOf('/') + 1)..] : id;
        yield return $"android:id/{name}";
        yield return $"{E2ESettings.E2EPackageId}:id/{name}";
    }

    private AppiumElement FindEditIn(AppiumElement dialog, string stage)
    {
        var field = dialog.FindElements(MobileBy.Id("android:id/edit"))
            .Concat(dialog.FindElements(By.ClassName("android.widget.EditText")))
            .Concat(dialog.FindElements(By.ClassName("androidx.appcompat.widget.AppCompatEditText")))
            .OfType<AppiumElement>()
            .FirstOrDefault(IsDisplayed);
        if (field is not null)
        {
            return field;
        }

        throw new InvalidOperationException($"Prompt field was not found during '{stage}'.");
    }

    private void HideKeyboardQuietly()
    {
        try
        {
            RequireDriver().HideKeyboard();
        }
        catch (Exception)
        {
        }
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
