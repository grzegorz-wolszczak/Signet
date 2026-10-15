using System.Threading.Tasks;

namespace Signet.App.ScreenPicker;

/// <summary>Takes an in-memory snapshot of the whole desktop.</summary>
public interface IScreenCapturer
{
    /// <summary>Captures the desktop; <see langword="null"/> when this capturer cannot (the reason goes to the log).</summary>
    Task<ScreenSnapshot?> CaptureAsync();
}
