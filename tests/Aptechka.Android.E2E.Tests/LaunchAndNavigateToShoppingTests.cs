namespace Aptechka.Android.E2E.Tests;

public sealed class AndroidE2EFactAttribute : FactAttribute
{
    public AndroidE2EFactAttribute()
    {
        if (!E2ESettings.IsConfigured)
        {
            Skip = "Requires scripts/test-android-e2e.ps1 (APTECHKA_E2E_DEVICE_ID).";
        }
    }
}

[Collection("AndroidE2E")]
public sealed class LaunchAndNavigateToShoppingTests
{
    private readonly AndroidSession session;

    public LaunchAndNavigateToShoppingTests(AndroidSession session)
    {
        this.session = session;
    }

    [AndroidE2EFact]
    public void LaunchAndNavigateToShopping()
    {
        const string stage = nameof(LaunchAndNavigateToShopping);
        try
        {
            session.WaitVisible(AutomationIds.MainPage, stage);

            session.WaitVisible(AutomationIds.MainShopping, stage).Click();
            session.WaitVisible(AutomationIds.ShoppingPage, stage);
            session.WaitVisible(AutomationIds.ShoppingSearch, stage);
            Assert.True(session.HasText("Покупки"), "Shopping page title 'Покупки' was not shown.");
            Assert.Equal(
                "Пока ничего покупать не нужно.",
                session.WaitVisible(AutomationIds.ShoppingActiveEmpty, stage).Text);
            Assert.Equal(
                "Недавно закрытых покупок нет.",
                session.WaitVisible(AutomationIds.ShoppingRecentEmpty, stage).Text);

            session.WaitVisible(AutomationIds.ShoppingAdd, stage).Click();
            session.WaitVisible(AutomationIds.ShoppingCandidateSearch, stage);
            session.WaitVisible(AutomationIds.ShoppingCandidates, stage);
            Assert.True(session.HasText("Добавить покупку"), "Picker title 'Добавить покупку' was not shown.");
            Assert.Equal(
                "Нет позиций, которые можно добавить.",
                session.WaitVisible(AutomationIds.ShoppingCandidatesEmpty, stage).Text);

            session.PressSystemBack();
            session.WaitGone(AutomationIds.ShoppingCandidateSearch, stage);
            session.WaitVisible(AutomationIds.ShoppingPage, stage);
            session.WaitVisible(AutomationIds.ShoppingSearch, stage);
            Assert.True(session.HasText("Покупки"), "Shopping page did not stay open after Back.");
            session.WaitVisible(AutomationIds.ShoppingActiveEmpty, stage);

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
