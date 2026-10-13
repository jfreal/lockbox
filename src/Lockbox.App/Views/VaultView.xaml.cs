using System.Windows.Controls;
using System.Windows.Input;
using Lockbox.App.ViewModels;

namespace Lockbox.App.Views;

public partial class VaultView : UserControl
{
    public VaultView() => InitializeComponent();

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    // Clicking a project while searching leaves search and shows that project.
    private void OnProjectListClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is VaultViewModel { IsSearching: true } vault)
            vault.SearchText = "";
    }
}
