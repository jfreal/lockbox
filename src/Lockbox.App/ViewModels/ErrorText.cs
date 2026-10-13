namespace Lockbox.App.ViewModels;

internal static class ErrorText
{
    /// <summary>
    /// Some Windows errors (Windows Hello's COM exceptions among them) arrive with an empty message.
    /// Never show a blank error: fall back to the error code.
    /// </summary>
    public static string Of(Exception ex) =>
        string.IsNullOrWhiteSpace(ex.Message)
            ? $"Something went wrong in Windows (error 0x{ex.HResult:X8})."
            : ex.Message.Trim();
}
