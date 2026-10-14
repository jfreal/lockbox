using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Lockbox.App.ViewModels;

namespace Lockbox.App.Views;

public partial class VaultView : UserControl
{
    public VaultView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged old)
                old.PropertyChanged -= OnVaultPropertyChanged;
            if (e.NewValue is INotifyPropertyChanged now)
                now.PropertyChanged += OnVaultPropertyChanged;
        };
    }

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

    // A new note starts in its title; an existing one at the end of its text.
    private void OnVaultPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(VaultViewModel.EditingNote) || sender is not VaultViewModel { EditingNote: { } editor })
            return;

        Dispatcher.BeginInvoke(() =>
        {
            var target = editor.IsNew ? NoteTitle : NoteBody;
            target.Focus();
            Keyboard.Focus(target);
            target.CaretIndex = target.Text.Length;
        }, DispatcherPriority.Input);
    }

    // One click opens a note. Keys keep their value hidden, so a double-click is needed to edit one.
    // Buttons inside the row handle their own clicks first, so this only sees clicks on the row itself.
    private void OnRowMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: SecretViewModel secret } || DataContext is not VaultViewModel vault)
            return;

        if (secret.IsNote ? e.ClickCount == 1 : e.ClickCount == 2)
        {
            vault.EditSecretCommand.Execute(secret);
            e.Handled = true;
        }
    }

    // Clicking a project while searching leaves search and shows that project.
    private void OnProjectListClicked(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is VaultViewModel { IsSearching: true } vault)
            vault.SearchText = "";
    }
}
