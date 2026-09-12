namespace Aptechka.Android.E2E.Tests;

[Collection("AndroidE2E")]
public sealed class ManualShoppingLifecycleTests
{
    private const string ItemName = "E2E Ибупрофен";
    private const string ItemForm = "таблетки";
    private const string ItemStrength = "200 мг";
    private const string ItemDetails = "таблетки, 200 мг";
    private const string ItemNote = "Купить в тестовой аптеке";
    private const string AbsentQuery = "q2a-absent-xyz";

    private static readonly string[] ActiveActions =
    [
        "Открыть позицию",
        "Снять ручную отметку",
        "Изменить заметку",
    ];

    private static readonly string[] RecentActions =
    [
        "Вернуть в покупки",
        "Открыть позицию",
        "Изменить заметку",
    ];

    private readonly AndroidSession session;

    public ManualShoppingLifecycleTests(AndroidSession session)
    {
        this.session = session;
    }

    [AndroidE2EFact]
    public void ManualShoppingLifecycle()
    {
        var stage = "start";
        try
        {
            stage = "open editor";
            session.WaitVisible(AutomationIds.MainPage, stage);
            session.WaitVisible(AutomationIds.MainAdd, stage).Click();
            session.WaitVisible(AutomationIds.ItemEditorPage, stage);
            session.WaitVisible(AutomationIds.ItemEditorName, stage);

            stage = "fill item";
            session.TypeInto(AutomationIds.ItemEditorName, ItemName, stage);
            session.TypeInto(AutomationIds.ItemEditorForm, ItemForm, stage);
            session.TypeInto(AutomationIds.ItemEditorStrength, ItemStrength, stage);

            stage = "assert keepInStock off";
            Assert.False(
                session.IsChecked(AutomationIds.ItemEditorKeepInStock, stage),
                "KeepInStock must stay off for a new E2E item.");

            stage = "save item";
            session.ScrollTo(AutomationIds.ItemEditorSave, stage).Click();
            session.WaitVisible(AutomationIds.MainPage, stage);
            session.WaitGone(AutomationIds.ItemEditorPage, stage);

            stage = "open shopping empty";
            session.WaitVisible(AutomationIds.MainShopping, stage).Click();
            session.WaitVisible(AutomationIds.ShoppingPage, stage);
            session.WaitVisible(AutomationIds.ShoppingSearch, stage);
            Assert.Equal(
                "Пока ничего покупать не нужно.",
                session.WaitVisible(AutomationIds.ShoppingActiveEmpty, stage).Text);
            Assert.Equal(
                "Недавно закрытых покупок нет.",
                session.WaitVisible(AutomationIds.ShoppingRecentEmpty, stage).Text);

            stage = "add candidate";
            session.WaitVisible(AutomationIds.ShoppingAdd, stage).Click();
            session.WaitVisible(AutomationIds.ShoppingCandidateSearch, stage);
            session.TypeInto(AutomationIds.ShoppingCandidateSearch, ItemName, stage);
            var candidate = session.WaitSingleVisible(AutomationIds.ShoppingCandidateRow, stage);
            session.AssertRowContains(candidate, stage, ItemName, ItemDetails);
            candidate.Click();

            stage = "active after add";
            session.WaitGone(AutomationIds.ShoppingCandidateSearch, stage);
            session.WaitVisible(AutomationIds.ShoppingPage, stage);
            session.WaitVisible(AutomationIds.ShoppingSearch, stage);
            var active = session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage);
            session.AssertRowContains(active, stage, ItemName, "Добавлено вручную");
            session.WaitVisible(AutomationIds.ShoppingRecentEmpty, stage);

            stage = "edit note";
            session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage).Click();
            session.ChooseActionSheetItem(ActiveActions, "Изменить заметку", stage, ItemName);
            session.SubmitPrompt(ItemNote, stage, "Заметка", "Сохранить");
            active = session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage);
            session.AssertRowContains(active, stage, ItemName, ItemNote, "Добавлено вручную");

            stage = "search active by note";
            session.TypeInto(AutomationIds.ShoppingSearch, ItemNote, stage);
            session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage);

            stage = "search active missing";
            session.TypeInto(AutomationIds.ShoppingSearch, AbsentQuery, stage);
            Assert.Equal(
                "Ничего не найдено.",
                session.WaitVisible(AutomationIds.ShoppingActiveEmpty, stage).Text);
            session.WaitGone(AutomationIds.ShoppingActiveRow, stage);

            stage = "clear active search";
            session.ClearField(AutomationIds.ShoppingSearch, stage);
            session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage);
            Assert.Equal(
                "Недавно закрытых покупок нет.",
                session.WaitVisible(AutomationIds.ShoppingRecentEmpty, stage).Text);

            stage = "clear manual mark";
            session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage).Click();
            session.ChooseActionSheetItem(ActiveActions, "Снять ручную отметку", stage, ItemName);
            session.WaitGone(AutomationIds.ShoppingActiveRow, stage);
            Assert.Equal(
                "Пока ничего покупать не нужно.",
                session.WaitVisible(AutomationIds.ShoppingActiveEmpty, stage).Text);
            var recent = session.WaitSingleVisible(AutomationIds.ShoppingRecentRow, stage);
            session.AssertRowContains(recent, stage, ItemName, ItemNote);

            stage = "search recent by note";
            session.TypeInto(AutomationIds.ShoppingSearch, ItemNote, stage);
            session.WaitSingleVisible(AutomationIds.ShoppingRecentRow, stage);
            session.ClearField(AutomationIds.ShoppingSearch, stage);
            session.WaitSingleVisible(AutomationIds.ShoppingRecentRow, stage);

            stage = "restore to active";
            session.WaitSingleVisible(AutomationIds.ShoppingRecentRow, stage).Click();
            session.ChooseActionSheetItem(RecentActions, "Вернуть в покупки", stage, ItemName);
            session.WaitGone(AutomationIds.ShoppingRecentRow, stage);
            active = session.WaitSingleVisible(AutomationIds.ShoppingActiveRow, stage);
            session.AssertRowContains(active, stage, ItemName, ItemNote, "Добавлено вручную");
            Assert.Equal(
                "Недавно закрытых покупок нет.",
                session.WaitVisible(AutomationIds.ShoppingRecentEmpty, stage).Text);

            stage = "back to main";
            session.PressSystemBack();
            session.WaitVisible(AutomationIds.MainPage, stage);
            session.WaitGone(AutomationIds.ShoppingPage, stage);
        }
        catch
        {
            session.SavePageSource(stage);
            throw;
        }
    }
}
