using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lockbox.App.Services;
using Lockbox.App.ViewModels;
using Lockbox.Core;
using Lockbox.Core.Crypto;
using Lockbox.Core.Vault;

namespace Lockbox.App;

/// <summary>
/// Debug-only: renders every screen with made-up data to PNGs, in light and dark.
/// Run: Lockbox.exe --snapshot &lt;folder&gt;. Uses a throwaway vault in %TEMP%, never the real one.
/// </summary>
internal static class Snapshots
{
    private const string DemoPassphrase = "demo passphrase for snapshots";

    private const string SampleNotes = """
        acme-web
        NETLIFY_AUTH_TOKEN=nfp_9fKq2LmZx81RtYvB0cWe
        RESEND_API_KEY = "re_7Hd2kQp9_xVb3NmLs"

        ## billing-api
        export OPENAI_API_KEY='sk-proj-4f8Kd0QwErTy7UiOpAsD'
        Stripe secret: sk_test_51NqLz8kPq2Xw
        postgres://billing-api:Tr0ub4dor@db.example.net:5432/billing-api

        Remember to rotate the Stripe key after the launch, and tell Sam.
        """;

    public static async Task RunAsync(string outDir)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Directory.CreateDirectory(outDir);
        var dataDir = Directory.CreateTempSubdirectory("lockbox-snapshot-").FullName;
        try
        {
            foreach (var dark in new[] { false, true })
                await RunThemeAsync(outDir, dataDir, dark);
        }
        finally
        {
            Directory.Delete(dataDir, recursive: true);
        }
    }

    private static async Task RunThemeAsync(string outDir, string dataDir, bool dark)
    {
        ThemeService.Override = dark;
        ThemeService.Apply();
        var suffix = dark ? "-dark" : "";
        var settings = new AppSettings
        {
            BackupFolder = Path.Combine(dataDir, "Dropbox" + suffix, "Lockbox"),
            LastPassphraseUnlock = DateTimeOffset.UtcNow.AddDays(-2),
            LastBackup = DateTimeOffset.UtcNow.AddMinutes(-3),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(settings.BackupFolder)!);
        var shell = new ShellViewModel(
            new VaultStore(Path.Combine(dataDir, $"vault{suffix}.lockbox"), new KdfParameters(1024, 1, 1)),
            settings, Path.Combine(dataDir, $"settings{suffix}.json"), new WindowsHelloService(), new SecureClipboard())
        {
            HelloAvailable = true,
        };

        var window = new MainWindow(shell, protectFromCapture: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000,
            Top = -30000,
            ShowActivated = false,
        };
        window.Show();

        var setup = new SetupViewModel(shell) { Passphrase = "orbit maple tunnel crisp lantern", Confirm = "orbit maple tunnel crisp lantern" };
        shell.CurrentScreen = setup;
        await CaptureAsync(window, outDir, "01-setup" + suffix);
        setup.IsHelloStep = true;
        await CaptureAsync(window, outDir, "02-setup-hello" + suffix);

        using (var created = shell.Store.Create(DemoPassphrase))
        {
            Seed(created.Data);
            created.Save();
            // Demo-only Hello slot so the unlock screen shows the Hello button. A software RSA key stands in for Hello.
            using var demoKey = RSA.Create(2048);
            await created.EnableHelloAsync("demo", key => demoKey.Encrypt(key, RSAEncryptionPadding.Pkcs1),
                (_, wrapped) => Task.FromResult(demoKey.Decrypt(wrapped, RSAEncryptionPadding.Pkcs1)));
        }

        shell.ShowUnlock(promptForHello: false);
        await CaptureAsync(window, outDir, "03-unlock" + suffix);
        shell.ShowUnlock(promptForHello: false, forcePassphrase: true);
        await CaptureAsync(window, outDir, "04-unlock-passphrase" + suffix);

        shell.BeginSession(shell.Store.UnlockWithPassphrase(DemoPassphrase), viaPassphrase: true);
        shell.EnterVault();
        var vault = shell.Vault!;
        vault.SelectedProject = vault.Projects.First(p => p.Name == "acme-web");
        vault.SelectedProject.Secrets.First(s => s.Name == "DATABASE_URL").IsRevealed = true;
        shell.ShowToast("Copied STRIPE_SECRET_KEY. Clipboard clears in 60 seconds.");
        await CaptureAsync(window, outDir, "05-vault" + suffix);
        shell.Toast = null;

        vault.SearchText = "stripe";
        await CaptureAsync(window, outDir, "06-search" + suffix);
        vault.SearchText = "";

        vault.OpenNote(vault.SelectedProject.Secrets.First(s => s.Name == "Deploy runbook"));
        await CaptureAsync(window, outDir, "12-note" + suffix);
        vault.CloseNote();

        vault.AddSecretCommand.Execute(null);
        if (shell.Sheet is SecretEditorSheet sheet)
        {
            sheet.Name = "SENTRY_DSN";
            sheet.Value = "https://a1b2c3@o12345.ingest.sentry.io/678";
        }

        await CaptureAsync(window, outDir, "07-add-secret" + suffix);
        shell.CloseSheet();

        vault.DeleteProjectCommand.Execute(null);
        await CaptureAsync(window, outDir, "08-delete-confirm" + suffix);
        shell.CloseSheet();

        var import = new ImportViewModel(shell, vault, shell.Session.Data) { RawText = SampleNotes };
        shell.CurrentScreen = import;
        await CaptureAsync(window, outDir, "09-import-paste" + suffix);
        import.FindSecretsCommand.Execute(null);
        await CaptureAsync(window, outDir, "10-import-review" + suffix);

        shell.CurrentScreen = new SettingsViewModel(shell);
        await CaptureAsync(window, outDir, "11-settings" + suffix);

        shell.Lock();
        window.Close();
    }

    private static void Seed(VaultData data)
    {
        static Project P(string name, params (string Name, string Value, bool Note)[] secrets) => new()
        {
            Name = name,
            Secrets = secrets.Select(s => new Secret { Name = s.Name, Value = s.Value, Kind = s.Note ? SecretKind.Note : SecretKind.KeyValue }).ToList(),
        };

        data.Projects.AddRange(
        [
            P("acme-web",
                ("NETLIFY_AUTH_TOKEN", "nfp_9fKq2LmZx81RtYvB0cWe", false),
                ("DATABASE_URL", "postgres://acme-web:s3cr3t-pw@db.acme-web.example:5432/prod", false),
                ("RESEND_API_KEY", "re_7Hd2kQp9_xVb3NmLs", false),
                ("STRIPE_SECRET_KEY", "sk_test_51NqLz8kPq2Xw", false),
                ("STRIPE_WEBHOOK_SECRET", "whsec_Qm4p0aZ8", false),
                ("Recovery codes", "4821-0937\n1170-5524\n9032-6618\n2289-4410", true),
                ("Deploy runbook", "Production deploys go out from main.\n\n1. Merge the PR and wait for CI.\n2. Netlify builds automatically. Check the deploy log for warnings.\n3. Run the smoke test: npm run smoke -- --env prod\n4. If the payment page fails, roll back from the Netlify dashboard.\n\nDatabase migrations run before the build. Back up first:\n  pg_dump $DATABASE_URL > backup.sql\n\nOn-call phone tree is in the shared drive under Ops.", true)),
            P("billing-api", ("OPENAI_API_KEY", "sk-proj-4f8Kd0QwErTy7UiOpAsD", false), ("CLOUDFLARE_API_TOKEN", "cf_Zx81RtYvB0", false)),
            P("mobile-app", ("GITHUB_TOKEN", "ghp_A1b2C3d4E5f6G7h8", false), ("SENTRY_DSN", "https://a@o1.ingest.sentry.io/2", false), ("STRIPE_SECRET_KEY", "sk_test_99Xw", false)),
            P("data-pipeline", ("FIREBASE_API_KEY", "AIzaSyD-123456", false)),
            P("status-page", ("TWILIO_AUTH_TOKEN", "a1b2c3d4e5", false), ("TWILIO_SID", "AC0000", false)),
            P("shop", ("SHOPIFY_TOKEN", "shpat_123", false)),
            P("side-project"),
            P("home-lab", ("ANTHROPIC_API_KEY", "sk-ant-demo", false), ("Server notes", "ssh deploy@10.0.0.4\nport 2222", true)),
        ]);
    }

    private static async Task CaptureAsync(Window window, string outDir, string name)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(400); // let the sheet animation finish
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

        var root = (FrameworkElement)window.Content;
        const double scale = 1.5;
        var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var file = File.Create(Path.Combine(outDir, name + ".png"));
        encoder.Save(file);
    }
}
