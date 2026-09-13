namespace Aptechka.Android.E2E.Tests;

internal static class AutomationIds
{
    public const string MainPage = "Main.Page";
    public const string MainShopping = "Main.Shopping";
    public const string MainAdd = "Main.Add";
    public const string ShoppingPage = "Shopping.Page";
    public const string ShoppingAdd = "Shopping.Add";
    public const string ShoppingSearch = "Shopping.Search";
    public const string ShoppingCandidateSearch = "Shopping.CandidateSearch";
    public const string ShoppingActive = "Shopping.Active";
    public const string ShoppingRecent = "Shopping.Recent";
    public const string ShoppingCandidates = "Shopping.Candidates";
    public const string ShoppingActiveEmpty = "Shopping.ActiveEmpty";
    public const string ShoppingRecentEmpty = "Shopping.RecentEmpty";
    public const string ShoppingCandidatesEmpty = "Shopping.CandidatesEmpty";
    public const string ShoppingCandidateRow = "Shopping.CandidateRow";
    public const string ShoppingActiveRow = "Shopping.ActiveRow";
    public const string ShoppingRecentRow = "Shopping.RecentRow";
    public const string ItemEditorPage = "ItemEditor.Page";
    public const string ItemEditorName = "ItemEditor.Name";
    public const string ItemEditorForm = "ItemEditor.Form";
    public const string ItemEditorStrength = "ItemEditor.Strength";
    public const string ItemEditorKeepInStock = "ItemEditor.KeepInStock";
    public const string ItemEditorSave = "ItemEditor.Save";
    public const string ItemEditorAddPackage = "ItemEditor.AddPackage";
    public const string ItemEditorPackageRow = "ItemEditor.PackageRow";
    public const string PackageEditorPage = "PackageEditor.Page";
    public const string PackageEditorLabel = "PackageEditor.Label";
    public const string PackageEditorStock = "PackageEditor.Stock";
    public const string PackageEditorSave = "PackageEditor.Save";
}

internal static class E2ESettings
{
    public const string E2EPackageId = "io.github.vakineti.aptechka.e2e";
    public const string ProductionPackageId = "io.github.vakineti.aptechka";

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APTECHKA_E2E_DEVICE_ID"));

    public static Uri AppiumUrl => new(
        FirstNonEmpty("APTECHKA_E2E_APPIUM_URL", "http://127.0.0.1:4723"),
        UriKind.Absolute);

    public static string DeviceId =>
        Required("APTECHKA_E2E_DEVICE_ID");

    public static string ApkPath
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("APTECHKA_E2E_APK_PATH");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "artifacts", "e2e", "android", "apk",
                "io.github.vakineti.aptechka.e2e-Signed.apk"));
        }
    }

    public static string ArtifactDir
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("APTECHKA_E2E_ARTIFACT_DIR");
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return Path.GetFullPath(configured);
            }

            return Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory,
                "..", "..", "..", "..", "..",
                "artifacts", "e2e", "android"));
        }
    }

    private static string Required(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is required. Run scripts/test-android-e2e.ps1.");

    private static string FirstNonEmpty(string name, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }
}
