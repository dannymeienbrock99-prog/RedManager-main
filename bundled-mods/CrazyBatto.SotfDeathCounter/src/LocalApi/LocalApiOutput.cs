using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using CrazyBatto.SotfDeathCounter.Core;

namespace CrazyBatto.SotfDeathCounter.LocalApi;

/// <summary>
/// Optional loopback-only API and OBS browser overlay. It is not started by the
/// core; the host project explicitly owns its lifetime.
/// </summary>
public sealed class LocalApiOutput : IDeathCounterOutput
{
    private readonly LocalApiOptions _options;
    private readonly Action<string> _log;
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private DeathCounterModule? _module;
    private string _html = string.Empty;
    private string _css = string.Empty;
    private string _javascript = string.Empty;

    public LocalApiOutput(LocalApiOptions? options = null, Action<string>? log = null)
    {
        _options = (options ?? new LocalApiOptions()).CloneNormalized();
        _log = log ?? (_ => { });
    }

    public bool IsRunning => _listener is not null;
    public string BaseUrl => $"http://127.0.0.1:{_options.Port}/";
    public string SnapshotUrl => $"{BaseUrl}api/v1/snapshot";
    public string OverlayUrl => $"{BaseUrl}overlay";

    public void Start(DeathCounterModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        if (IsRunning)
        {
            return;
        }

        _module = module;
        if (_options.EnableObsOverlay)
        {
            _html = ReadEmbeddedText("overlay.html");
            _css = ReadEmbeddedText("overlay.css");
            _javascript = ReadEmbeddedText("overlay.js");
        }

        _cancellation = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, _options.Port);
        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
        _log($"Lokale Todeszähler-API: {SnapshotUrl}");
        if (_options.EnableObsOverlay)
        {
            _log($"OBS-Overlay: {OverlayUrl}");
        }
    }

    public void Stop()
    {
        try
        {
            _cancellation?.Cancel();
            _listener?.Stop();
        }
        catch
        {
            // Ignore shutdown races.
        }
        finally
        {
            _listener = null;
            _module = null;
            _cancellation?.Dispose();
            _cancellation = null;
            _acceptLoop = null;
        }
    }

    private async Task AcceptLoopAsync()
    {
        var cancellation = _cancellation;
        if (cancellation is null)
        {
            return;
        }

        var cancellationToken = cancellation.Token;
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
            }
            catch when (cancellationToken.IsCancellationRequested || _listener is null)
            {
                return;
            }
            catch (Exception ex)
            {
                _log($"Lokale API konnte keine Verbindung annehmen: {ex.Message}");
                await Task.Delay(250).ConfigureAwait(false);
                continue;
            }

            _ = Task.Run(() => HandleClientAsync(client));
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            client.ReceiveTimeout = 3000;
            client.SendTimeout = 3000;

            try
            {
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
                var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine))
                {
                    return;
                }

                string? headerLine;
                do
                {
                    headerLine = await reader.ReadLineAsync().ConfigureAwait(false);
                } while (!string.IsNullOrEmpty(headerLine));

                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                {
                    await WriteResponseAsync(stream, 400, "text/plain; charset=utf-8", "Bad Request").ConfigureAwait(false);
                    return;
                }

                var method = parts[0].ToUpperInvariant();
                var path = parts[1].Split('?', 2)[0];
                if (method == "OPTIONS")
                {
                    await WriteResponseAsync(stream, 204, "text/plain", string.Empty).ConfigureAwait(false);
                    return;
                }

                if (method != "GET")
                {
                    await WriteResponseAsync(stream, 405, "text/plain; charset=utf-8", "Method Not Allowed").ConfigureAwait(false);
                    return;
                }

                await RouteAsync(stream, path).ConfigureAwait(false);
            }
            catch
            {
                // OBS browser sources may cancel a request while a scene reloads.
            }
        }
    }

    private Task RouteAsync(NetworkStream stream, string path)
    {
        var module = _module;
        if (module is null)
        {
            return WriteResponseAsync(stream, 503, "application/json; charset=utf-8", "{\"ok\":false}");
        }

        switch (path.ToLowerInvariant())
        {
            case "/api/stats":
            case "/api/v1/snapshot":
                return WriteResponseAsync(
                    stream,
                    200,
                    "application/json; charset=utf-8",
                    module.GetSnapshotJson());

            case "/api/health":
            case "/api/v1/health":
                return WriteResponseAsync(
                    stream,
                    200,
                    "application/json; charset=utf-8",
                    $"{{\"ok\":true,\"utc\":\"{DateTime.UtcNow:O}\"}}");

            case "/":
            case "/overlay":
            case "/overlay/":
                return _options.EnableObsOverlay
                    ? WriteResponseAsync(stream, 200, "text/html; charset=utf-8", _html)
                    : WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Overlay disabled");

            case "/overlay.css":
                return _options.EnableObsOverlay
                    ? WriteResponseAsync(stream, 200, "text/css; charset=utf-8", _css)
                    : WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found");

            case "/overlay.js":
                return _options.EnableObsOverlay
                    ? WriteResponseAsync(stream, 200, "application/javascript; charset=utf-8", _javascript)
                    : WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found");

            default:
                return WriteResponseAsync(stream, 404, "text/plain; charset=utf-8", "Not Found");
        }
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string contentType,
        string content)
    {
        var body = Encoding.UTF8.GetBytes(content);
        var statusText = statusCode switch
        {
            200 => "OK",
            204 => "No Content",
            400 => "Bad Request",
            404 => "Not Found",
            405 => "Method Not Allowed",
            503 => "Service Unavailable",
            _ => "OK"
        };

        var headers = new StringBuilder()
            .Append("HTTP/1.1 ").Append(statusCode).Append(' ').Append(statusText).Append("\r\n")
            .Append("Content-Type: ").Append(contentType).Append("\r\n")
            .Append("Content-Length: ").Append(body.Length).Append("\r\n")
            .Append("Cache-Control: no-store, no-cache, must-revalidate\r\n")
            .Append("Pragma: no-cache\r\n")
            .Append("Access-Control-Allow-Origin: *\r\n")
            .Append("Access-Control-Allow-Methods: GET, OPTIONS\r\n")
            .Append("X-Content-Type-Options: nosniff\r\n")
            .Append("Connection: close\r\n\r\n");

        var headerBytes = Encoding.ASCII.GetBytes(headers.ToString());
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length).ConfigureAwait(false);
        if (body.Length > 0)
        {
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static string ReadEmbeddedText(string suffix)
    {
        var assembly = typeof(LocalApiOutput).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
        {
            throw new InvalidOperationException($"Embedded overlay resource missing: {suffix}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Overlay resource cannot be opened: {resourceName}");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void Dispose()
    {
        Stop();
    }
}
