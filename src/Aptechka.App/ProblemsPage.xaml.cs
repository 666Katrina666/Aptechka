using System.ComponentModel;
using Aptechka.App.ViewModels;

namespace Aptechka.App;

public partial class ProblemsPage : ContentPage
{
    private readonly ProblemsViewModel viewModel;
    private readonly IServiceProvider services;

    public ProblemsPage(ProblemsViewModel viewModel, IServiceProvider services)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        this.services = services;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }

    private async void OnAddClicked(object? sender, EventArgs e) =>
        await OpenEditorAsync(null);

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not ProblemListRow row)
        {
            if (sender is CollectionView emptyList)
            {
                emptyList.SelectedItem = null;
            }

            return;
        }

        if (sender is CollectionView list)
        {
            list.SelectedItem = null;
        }

        await OpenEditorAsync(row.Id);
    }

    private async Task OpenEditorAsync(string? problemId)
    {
        var page = services.GetRequiredService<ProblemEditorPage>();
        await page.InitializeAsync(problemId);
        await Navigation.PushAsync(page);
    }
}
