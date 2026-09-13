namespace Aptechka.Android.E2E.Tests;

[Collection("AndroidE2E")]
public sealed class KeepInStockShoppingLifecycleTests : IClassFixture<AndroidSession>
{
    private const string ItemName = "E2E Обязательный запас";
    private const string ItemForm = "таблетки";
    private const string ItemStrength = "100 мг";
    private const string PackageLabel = "E2E пригодная упаковка";

    private static readonly string[] AutomaticActions =
    [
        "Открыть позицию",
        "Добавить ручную отметку",
    ];

    private readonly AndroidSession session;

    public KeepInStockShoppingLifecycleTests(AndroidSession session)
    {
        this.session = session;
    }

    [AndroidE2EFact]
    public void KeepInStockShoppingLifecycle()
    {
        var stage = "start";
        try
        {
            stage = "open editor";
            session.WaitVisible(AutomationIds.MainPage, stage);
            session.WaitVisible(AutomationIds.MainAdd, stage).Click();
            session.WaitVisible(AutomationIds.ItemEditorPage, stage);
            session.WaitVisible(AutomationIds.ItemEditorName, stage);

            stage = "create keepInStock item";
            session.TypeInto(AutomationIds.ItemEditorName, ItemName, stage);
            session.TypeInto(AutomationIds.ItemEditorForm, ItemForm, stage);
            session.TypeInto(AutomationIds.ItemEditorStrength, ItemStrength, stage);
            session.SetChecked(AutomationIds.ItemEditorKeepInStock, true, stage);
            session.ScrollTo(AutomationIds.ItemEditorSave, stage).Click();
            session.WaitVisible(AutomationIds.MainPage, stage);
            session.WaitGone(AutomationIds.ItemEditorPage, stage);

            stage = "attention after keepInStock missing";
            session.WaitVisible(AutomationIds.MainAttention, stage);
            var attention = session.WaitSingleVisible(AutomationIds.MainAttentionRow, stage);
            session.AssertRowContains(attention, stage, ItemName, "Обязательный запас закончился");

            stage = "automatic shopping row";
            session.WaitVisible(AutomationIds.MainShopping, stage).Click();
            session.WaitVisible(AutomationIds.ShoppingPage, stage);
            session.WaitVisible(AutomationIds.ShoppingSearch, stage);
            var active = session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage);
            session.AssertRowContains(
                active,
                stage,
                ItemName,
                "Нет дома",
                "Обязательный запас закончился");
            var texts = session.VisibleTexts(active);
            if (texts.Any(text => text.Contains("Добавлено вручную", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"Automatic row during '{stage}' unexpectedly contained 'Добавлено вручную'.");
            }

            Assert.Equal(
                "Недавно закрытых покупок нет.",
                session.WaitVisible(AutomationIds.ShoppingRecentEmpty, stage).Text);

            stage = "open item from shopping";
            session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage).Click();
            session.ChooseActionSheetItem(AutomaticActions, "Открыть позицию", stage, ItemName);
            session.WaitVisible(AutomationIds.ItemEditorPage, stage);
            Assert.True(
                session.IsChecked(AutomationIds.ItemEditorKeepInStock, stage),
                "KeepInStock must stay on after opening the automatic shopping item.");

            stage = "add usable package";
            session.ScrollTo(AutomationIds.ItemEditorAddPackage, stage).Click();
            session.WaitVisible(AutomationIds.PackageEditorPage, stage);
            session.TypeInto(AutomationIds.PackageEditorLabel, PackageLabel, stage);
            var packagePage = session.WaitVisible(AutomationIds.PackageEditorPage, stage);
            if (session.VisibleTexts(packagePage).Any(text => text.Contains("Точность срока", StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"Expiration was on during '{stage}'.");
            }

            session.WaitPickerValue(AutomationIds.PackageEditorStock, "В наличии", stage);
            session.ScrollTo(AutomationIds.PackageEditorSave, stage).Click();
            session.WaitVisible(AutomationIds.ItemEditorPage, stage);
            session.WaitGone(AutomationIds.PackageEditorPage, stage);

            stage = "package row";
            var packageRow = session.WaitSingleVisible(AutomationIds.ItemEditorPackageRow, stage);
            session.AssertRowContains(packageRow, stage, PackageLabel, "В наличии", "Срок не указан");

            stage = "shopping after usable package";
            session.PressSystemBack();
            session.WaitVisible(AutomationIds.ShoppingPage, stage);
            session.WaitGone(AutomationIds.ShoppingActiveRow, stage);
            Assert.Equal(
                "Пока ничего покупать не нужно.",
                session.WaitVisible(AutomationIds.ShoppingActiveEmpty, stage).Text);
            Assert.Equal(
                "Недавно закрытых покупок нет.",
                session.WaitVisible(AutomationIds.ShoppingRecentEmpty, stage).Text);

            stage = "back to main";
            session.PressSystemBack();
            session.WaitVisible(AutomationIds.MainPage, stage);
            session.WaitGone(AutomationIds.ShoppingPage, stage);
            session.WaitGone(AutomationIds.MainAttentionRow, stage);
            session.WaitGone(AutomationIds.MainAttention, stage);
        }
        catch
        {
            session.SavePageSource(stage);
            throw;
        }
    }
}
