using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Lockbox.App.Controls;

/// <summary>Small attached properties used by the styles and views.</summary>
public static class Assist
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius", typeof(CornerRadius), typeof(Assist), new FrameworkPropertyMetadata(new CornerRadius(0)));

    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);

    /// <summary>Moves keyboard focus to the element once it is loaded and visible.</summary>
    public static readonly DependencyProperty FocusOnLoadProperty = DependencyProperty.RegisterAttached(
        "FocusOnLoad", typeof(bool), typeof(Assist), new PropertyMetadata(false, OnFocusOnLoadChanged));

    public static bool GetFocusOnLoad(DependencyObject d) => (bool)d.GetValue(FocusOnLoadProperty);
    public static void SetFocusOnLoad(DependencyObject d, bool value) => d.SetValue(FocusOnLoadProperty, value);

    private static void OnFocusOnLoadChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element || e.NewValue is not true)
            return;

        element.Loaded += (_, _) => FocusSoon(element);
        element.IsVisibleChanged += (_, args) =>
        {
            if (args.NewValue is true)
                FocusSoon(element);
        };
    }

    private static void FocusSoon(FrameworkElement element) =>
        element.Dispatcher.BeginInvoke(() =>
        {
            if (!element.IsVisible)
                return;
            element.Focus();
            Keyboard.Focus(element);
            if (element is TextBox box)
                box.CaretIndex = box.Text.Length;
        }, System.Windows.Threading.DispatcherPriority.Input);
}

/// <summary>
/// PasswordBox.Password is not bindable on purpose. This pushes it one way into the view model,
/// and clears the box when the view model resets the value to empty.
/// </summary>
public static class PasswordBoxAssist
{
    public static readonly DependencyProperty PasswordProperty = DependencyProperty.RegisterAttached(
        "Password", typeof(string), typeof(PasswordBoxAssist),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPasswordChanged));

    public static string GetPassword(DependencyObject d) => (string)d.GetValue(PasswordProperty);
    public static void SetPassword(DependencyObject d, string value) => d.SetValue(PasswordProperty, value);

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.RegisterAttached(
        "IsEmpty", typeof(bool), typeof(PasswordBoxAssist), new PropertyMetadata(true));

    public static bool GetIsEmpty(DependencyObject d) => (bool)d.GetValue(IsEmptyProperty);
    public static void SetIsEmpty(DependencyObject d, bool value) => d.SetValue(IsEmptyProperty, value);

    // A class handler sees typing in every PasswordBox, including ones whose bound value never
    // changes from the empty default (a per-box hook added in the change callback would miss those).
    static PasswordBoxAssist() =>
        EventManager.RegisterClassHandler(typeof(PasswordBox), PasswordBox.PasswordChangedEvent, new RoutedEventHandler(OnBoxPasswordChanged));

    private static void OnBoxPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetIsEmpty(box, box.SecurePassword.Length == 0);
        if (GetPassword(box) != box.Password)
            box.SetCurrentValue(PasswordProperty, box.Password);
    }

    private static void OnPasswordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box)
            return;

        var value = e.NewValue as string ?? "";
        if (box.Password != value)
            box.Password = value;
        SetIsEmpty(box, value.Length == 0);
    }
}
