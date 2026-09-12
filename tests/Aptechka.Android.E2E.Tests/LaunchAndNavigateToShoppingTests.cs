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

public sealed class LaunchAndNavigateToShoppingTests : IClassFixture<AndroidSession>
{
    private readonly AndroidSession session;

    public LaunchAndNavigateToShoppingTests(AndroidSession session)
    {
        this.session = session;
    }

    [AndroidE2EFact]
    public void LaunchAndNavigateToShopping()
    {
        session.WaitVisible(AutomationIds.MainPage);

        session.WaitVisible(AutomationIds.MainShopping).Click();
        session.WaitVisible(AutomationIds.ShoppingPage);
        session.WaitVisible(AutomationIds.ShoppingSearch);
        Assert.True(session.HasText("Покупки"), "Shopping page title 'Покупки' was not shown.");
        Assert.Equal(
            "Пока ничего покупать не нужно.",
            session.WaitVisible(AutomationIds.ShoppingActiveEmpty).Text);
        Assert.Equal(
            "Недавно закрытых покупок нет.",
            session.WaitVisible(AutomationIds.ShoppingRecentEmpty).Text);

        session.WaitVisible(AutomationIds.ShoppingAdd).Click();
        session.WaitVisible(AutomationIds.ShoppingCandidateSearch);
        session.WaitVisible(AutomationIds.ShoppingCandidates);
        Assert.True(session.HasText("Добавить покупку"), "Picker title 'Добавить покупку' was not shown.");
        Assert.Equal(
            "Нет позиций, которые можно добавить.",
            session.WaitVisible(AutomationIds.ShoppingCandidatesEmpty).Text);

        session.PressSystemBack();
        session.WaitGone(AutomationIds.ShoppingCandidateSearch);
        session.WaitVisible(AutomationIds.ShoppingPage);
        session.WaitVisible(AutomationIds.ShoppingSearch);
        Assert.True(session.HasText("Покупки"), "Shopping page did not stay open after Back.");
        session.WaitVisible(AutomationIds.ShoppingActiveEmpty);

        session.PressSystemBack();
        session.WaitVisible(AutomationIds.MainPage);
        session.WaitGone(AutomationIds.ShoppingPage);
    }
}
