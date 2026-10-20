using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Lockbox.App.Services;

/// <summary>
/// Copies text so Windows keeps it out of clipboard history (Win+V) and cloud clipboard sync,
/// then clears it after <see cref="ClearAfter"/> if it is still what we put there.
/// </summary>
public sealed class SecureClipboard
{
    public static readonly TimeSpan ClearAfter = TimeSpan.FromSeconds(60);

    private readonly DispatcherTimer _timer;
    private string? _copied;

    public SecureClipboard()
    {
        _timer = new DispatcherTimer { Interval = ClearAfter };
        _timer.Tick += (_, _) => ClearIfOurs();
    }

    public void Copy(string text)
    {
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        // Documented clipboard formats: https://learn.microsoft.com/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));

        Retry(() => Clipboard.SetDataObject(data, copy: true));
        _copied = text;
        _timer.Stop();
        _timer.Start();
    }

    public void ClearIfOurs()
    {
        _timer.Stop();
        if (_copied is null)
            return;

        var copied = _copied;
        _copied = null;
        Retry(() =>
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == copied)
                Clipboard.Clear();
        });
    }

    // Another app can hold the clipboard open for a moment.
    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (ExternalException) when (attempt < 5)
            {
                Thread.Sleep(40);
            }
            catch (ExternalException)
            {
                return;
            }
        }
    }
}
