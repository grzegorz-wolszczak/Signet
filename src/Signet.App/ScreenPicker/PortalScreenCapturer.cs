using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace Signet.App.ScreenPicker;

/// <summary>
/// Captures the desktop through the <c>org.freedesktop.portal.Screenshot</c> portal (the only sanctioned way on
/// Wayland). The portal hands back a file: it is decoded into memory and deleted at once, nothing is kept on disk.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class PortalScreenCapturer : IScreenCapturer
{
    private const string Destination = "org.freedesktop.portal.Desktop";
    private const string ObjectPath = "/org/freedesktop/portal/desktop";
    private static readonly TimeSpan s_timeout = TimeSpan.FromSeconds(60);

    public async Task<ScreenSnapshot?> CaptureAsync()
    {
        string? path = await RequestScreenshotAsync().ConfigureAwait(false);
        if (path is null)
        {
            return null;
        }

        try
        {
            return await Task.Run(() => Decode(path)).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException exception)
            {
                Log.Warning(exception, "Could not delete the portal screenshot {Path}", path);
            }
        }
    }

    // The screenshot is assumed to cover the desktop starting at its top-left corner (0, 0).
    private static ScreenSnapshot? Decode(string path)
    {
        using SKBitmap? decoded = SKBitmap.Decode(path);
        if (decoded is null)
        {
            return null;
        }

        using SKBitmap bgra = decoded.ColorType == SKColorType.Bgra8888 ? decoded.Copy() : decoded.Copy(SKColorType.Bgra8888);
        byte[] pixels = new byte[bgra.Width * bgra.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bgra.GetPixels(), pixels, 0, pixels.Length);
        return new ScreenSnapshot(pixels, bgra.Width, bgra.Height, 0, 0);
    }

    // Asks for a non-interactive screenshot and waits for the Response signal of the request; returns the file path.
    private static async Task<string?> RequestScreenshotAsync()
    {
        if (DBusAddress.Session is not { } address)
        {
            Log.Warning("No D-Bus session bus, the screenshot portal is unavailable");
            return null;
        }

        using DBusConnection connection = new(address);
        await connection.ConnectAsync().ConfigureAwait(false);

        string token = "signet" + Ulid.NewUlid().ToString().ToLowerInvariant();
        string sender = connection.UniqueName![1..].Replace('.', '_');
        string requestPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";

        TaskCompletionSource<string?> response = new(TaskCreationOptions.RunContinuationsAsynchronously);
        MatchRule rule = new()
        {
            Type = MessageType.Signal,
            Sender = Destination,
            Path = requestPath,
            Interface = "org.freedesktop.portal.Request",
            Member = "Response",
        };
        using IDisposable subscription = await connection.AddMatchAsync(
            rule,
            (message, _) => ReadResponse(message),
            notification =>
            {
                if (notification.HasValue)
                {
                    response.TrySetResult(notification.Value);
                }
                else if (notification.IsCompletion)
                {
                    response.TrySetException(notification.Exception);
                }
            },
            false,
            ObserverFlags.None).ConfigureAwait(false);

        await connection.CallMethodAsync(CreateScreenshotCall(connection, token)).ConfigureAwait(false);

        using CancellationTokenSource timeout = new(s_timeout);
        await using (timeout.Token.Register(() => response.TrySetResult(null)).ConfigureAwait(false))
        {
            string? uri = await response.Task.ConfigureAwait(false);
            return uri is not null && Uri.TryCreate(uri, UriKind.Absolute, out Uri? file) && file.IsFile
                ? file.LocalPath
                : null;
        }
    }

    private static MessageBuffer CreateScreenshotCall(DBusConnection connection, string token)
    {
        MessageWriter writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(Destination, ObjectPath, "org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}");
        writer.WriteString(string.Empty);
        Dictionary<string, VariantValue> options = new()
        {
            ["handle_token"] = token,
            ["interactive"] = false,
            ["modal"] = false,
        };
        writer.WriteDictionary(options);
        return writer.CreateMessage();
    }

    // Response(u response, a{sv} results): response 0 = success, results["uri"] = file:// URI.
    private static string? ReadResponse(Message message)
    {
        Reader reader = message.GetBodyReader();
        uint code = reader.ReadUInt32();
        Dictionary<string, VariantValue> results = reader.ReadDictionaryOfStringToVariantValue();
        if (code != 0)
        {
            Log.Information("The screenshot portal request ended with code {Code}", code.ToString(CultureInfo.InvariantCulture));
            return null;
        }

        return results.TryGetValue("uri", out VariantValue uri) ? uri.GetString() : null;
    }
}
