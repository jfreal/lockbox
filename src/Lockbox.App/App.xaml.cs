using System.Windows;
using Lockbox.App.Services;
using Lockbox.App.ViewModels;
using Lockbox.Core;
using Lockbox.Core.Crypto;
using Lockbox.Core.Vault;

namespace Lockbox.App;

public partial class App : Application
{
    private const string InstanceName = @"Local\Lockbox.SingleInstance";
    private const string ActivateEventName = @"Local\Lockbox.Activate";

    private Mutex? _instance;
    private EventWaitHandle? _activate;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

#if DEBUG
        var snapshot = Array.IndexOf(e.Args, "--snapshot");
        if (snapshot >= 0 && snapshot + 1 < e.Args.Length)
        {
            await Snapshots.RunAsync(e.Args[snapshot + 1]);
            Shutdown();
            return;
        }

        var selfTest = Array.IndexOf(e.Args, "--selftest");
        if (selfTest >= 0 && selfTest + 1 < e.Args.Length)
        {
            Shutdown(await SelfTest.RunAsync(e.Args[selfTest + 1]));
            return;
        }

        var helloProbe = Array.IndexOf(e.Args, "--hello-probe");
        if (helloProbe >= 0 && helloProbe + 1 < e.Args.Length)
        {
            Shutdown(await HelloProbe.RunAsync(e.Args[helloProbe + 1]));
            return;
        }
#endif

        // One window at a time. A second launch brings the first one forward.
        _instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var other))
                other.Set();
            Shutdown();
            return;
        }

        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => Dispatcher.BeginInvoke(BringToFront), null, Timeout.Infinite, executeOnlyOnce: false);

        // Show the problem instead of closing. Messages never include secret values.
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            if (MainWindow?.DataContext is ShellViewModel current)
                current.ShowToast(ErrorText.Of(args.Exception), isLong: true);
        };

        ThemeService.Initialize();
        var settings = AppSettings.Load(AppPaths.SettingsPath);
        var shell = new ShellViewModel(
            new VaultStore(AppPaths.VaultPath, KdfParameters.Default),
            settings,
            AppPaths.SettingsPath,
            new WindowsHelloService(),
            new SecureClipboard());

        var window = new MainWindow(shell);
        MainWindow = window;
        window.Show();
        await shell.StartAsync();
    }

    private void BringToFront()
    {
        if (MainWindow is not { } window)
            return;
        if (window.WindowState == WindowState.Minimized)
            window.WindowState = WindowState.Normal;
        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activate?.Dispose();
        _instance?.Dispose();
        base.OnExit(e);
    }
}
