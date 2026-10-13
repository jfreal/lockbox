using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Lockbox.App.Services;
using Lockbox.App.ViewModels;
using Microsoft.Win32;

namespace Lockbox.App;

public partial class MainWindow : Window
{
    private readonly ShellViewModel _shell;
    private readonly DispatcherTimer _idleTimer = new() { Interval = TimeSpan.FromSeconds(10) };

    public MainWindow(ShellViewModel shell, bool protectFromCapture = true)
    {
        InitializeComponent();
        _shell = shell;
        DataContext = shell;

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (protectFromCapture)
                NativeMethods.SetWindowDisplayAffinity(hwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            ThemeService.PaintTitleBar(this);
        };

        InputManager.Current.PreProcessInput += OnInput;
        _idleTimer.Tick += (_, _) => _shell.LockIfIdle();
        _idleTimer.Start();
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        PreviewKeyDown += OnPreviewKeyDown;

        Closed += (_, _) =>
        {
            _idleTimer.Stop();
            InputManager.Current.PreProcessInput -= OnInput;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            _shell.Clipboard.ClearIfOurs();
            _shell.Lock();
        };
    }

    private void OnInput(object sender, PreProcessInputEventArgs e)
    {
        if (e.StagingItem.Input is KeyboardEventArgs or MouseButtonEventArgs or MouseWheelEventArgs or MouseEventArgs)
            _shell.Touch();
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
            Dispatcher.BeginInvoke(_shell.Lock);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            Dispatcher.BeginInvoke(_shell.Lock);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _shell.Sheet is { } sheet)
        {
            sheet.Cancel();
            e.Handled = true;
        }
        else if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _shell.Lock();
            e.Handled = true;
        }
    }

    private void OnOverlayClicked(object sender, MouseButtonEventArgs e) => _shell.Sheet?.Cancel();
}
