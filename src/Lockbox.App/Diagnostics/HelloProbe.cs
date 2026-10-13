using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Interop;
using Lockbox.App.Services;

namespace Lockbox.App;

/// <summary>
/// Debug-only: runs Lockbox's real Windows Hello path on this PC. Encrypts a random value to the
/// Hello key and decrypts it twice (prompts expected). Never touches the vault or creates keys.
/// Run: Lockbox.exe --hello-probe &lt;report.txt&gt;. Exit code 0 when the round trip works.
/// </summary>
internal static class HelloProbe
{
    public static async Task<int> RunAsync(string reportPath)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var log = new List<string>();
        var clock = Stopwatch.StartNew();
        void Log(string line)
        {
            // Written line by line so a hang still leaves a trail.
            log.Add($"{clock.Elapsed.TotalSeconds,6:0.0}s  {line}");
            File.WriteAllLines(reportPath, log);
        }

        var window = new Window
        {
            Title = "Lockbox: Windows Hello test",
            Width = 460,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Content = new System.Windows.Controls.TextBlock
            {
                Text = "Testing Windows Hello. Approve each prompt with your face, fingerprint, or PIN.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
                FontSize = 14,
            },
        };
        window.Show();
        window.Activate();
        var hwnd = new WindowInteropHelper(window).Handle;
        var hello = new WindowsHelloService();
        var ok = false;

        try
        {
            Log($"Windows Hello supported: {await hello.IsAvailableAsync()}");
            var keyName = hello.DefaultKeyName();
            Log("Default Hello decryption key: found");

            var secret = RandomNumberGenerator.GetBytes(32);
            var wrapped = hello.Wrap(secret, keyName);
            Log($"Wrap: {wrapped.Length} bytes, no prompt");

            Log("Unwrap #1 (prompt expected)...");
            var first = await hello.UnwrapAsync(wrapped, hwnd, keyName);
            Log($"Unwrap #1: matches = {first.AsSpan().SequenceEqual(secret)}");

            Log("Unwrap #2 (prompt expected)...");
            var second = await hello.UnwrapAsync(wrapped, hwnd, keyName);
            Log($"Unwrap #2: matches = {second.AsSpan().SequenceEqual(secret)}");

            ok = first.AsSpan().SequenceEqual(secret) && second.AsSpan().SequenceEqual(secret);
        }
        catch (HelloCanceledException)
        {
            Log("Canceled at the Windows Hello prompt");
        }
        catch (Exception ex)
        {
            Log($"FAILED: {ex.GetType().Name} 0x{ex.HResult:X8}: {ex.Message}");
        }
        finally
        {
            Log(ok ? "RESULT: OK" : "RESULT: FAILED");
            window.Close();
        }

        return ok ? 0 : 1;
    }
}
