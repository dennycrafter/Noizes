using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Noizes;

/// <summary>
/// Tiny HTTP server for hooks, scripts and the Chrome extension.
/// Raw TCP on 127.0.0.1 (no admin rights / URL ACL needed, unlike HttpListener).
/// </summary>
public class HttpServer
{
    TcpListener _listener;
    CancellationTokenSource _cts;
    public int Port { get; private set; }
    public bool DryRun { get; set; }

    /// <summary>False when the last Start could not bind (e.g. the port is already taken).</summary>
    public bool IsListening { get; private set; }

    public void Start(int port)
    {
        Stop();
        Port = port;
        IsListening = false;
        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Loopback, port);
        try
        {
            _listener.Start();
        }
        catch (SocketException ex)
        {
            // another program (or a hung copy of Noizes) owns the port — stay alive and say so
            _listener = null;
            Logger.Info($"http server NOT listening on port {port}: {ex.Message} (port already in use?)");
            return;
        }
        IsListening = true;
        _ = AcceptLoop(_cts.Token);
        Logger.Info($"http server listening on http://127.0.0.1:{port}");
    }

    public void Stop()
    {
        try
        {
            if (_cts != null) _cts.Cancel();
            _listener?.Stop();
        }
        catch { }
        _listener = null;
        IsListening = false;
    }

    async Task AcceptLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener != null)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch { break; }
            _ = Task.Run(() => Handle(client));
        }
    }

    async Task Handle(TcpClient client)
    {
        try
        {
            using (client)
            {
                client.ReceiveTimeout = 5000;
                client.SendTimeout = 5000;
                var stream = client.GetStream();

                var reqLine = await ReadLine(stream);
                if (reqLine.Length == 0) return;
                var parts = reqLine.Split(' ');
                if (parts.Length < 2) return;
                var method = parts[0];
                var rawPath = parts[1];

                // read headers, keep Content-Length
                int contentLength = 0;
                while (true)
                {
                    var h = await ReadLine(stream);
                    if (h.Length == 0) break;
                    if (h.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(h.Substring(15).Trim(), out contentLength);
                }
                if (contentLength > 0 && contentLength < 65536)
                {
                    var body = new byte[contentLength];
                    int read = 0;
                    while (read < contentLength)
                    {
                        int n = await stream.ReadAsync(body, read, body.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                }

                var path = rawPath.Split('?')[0];
                var query = rawPath.Contains('?') ? rawPath.Split('?')[1] : "";
                var (code, json) = Route(method, path, query);
                await WriteResponse(stream, code, json);
            }
        }
        catch (Exception ex)
        {
            Logger.Info("http handle error: " + ex.Message);
        }
    }

    (int code, string json) Route(string method, string path, string query)
    {
        if (method == "OPTIONS") return (204, "");
        if (path == "/health") return (200, "{\"ok\":true,\"app\":\"Noizes\"}");
        if (path.StartsWith("/event/") && (method == "GET" || method == "POST"))
        {
            var id = path.Substring(7);
            if (EventRegistry.Get(id) == null)
                return (404, JsonFor(id, new DispatchResult(false, "unknown-event")));
            var res = HandleEvent(id, query);
            return (res.Reason == "unknown-event" ? 404 : 200, JsonFor(id, res));
        }
        return (404, "{\"ok\":false,\"error\":\"not-found\"}");
    }

    DispatchResult HandleEvent(string id, string query)
    {
        if (DryRun)
        {
            Logger.Info($"[dry-run] received event {id}");
            return new DispatchResult(false, "dry-run");
        }
        string[] focusOverride = null;
        foreach (var kv in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = kv.IndexOf('=');
            if (i > 0 && kv.Substring(0, i) == "app")
                focusOverride = new[] { Uri.UnescapeDataString(kv.Substring(i + 1)) };
        }
        return EventBus.Dispatch(id, focusOverride);
    }

    static string JsonFor(string id, DispatchResult r) =>
        $"{{\"ok\":true,\"event\":\"{id}\",\"played\":{(r.Played ? "true" : "false")},\"reason\":\"{r.Reason}\"}}";

    static async Task<string> ReadLine(NetworkStream s)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            int n = await s.ReadAsync(buf, 0, 1);
            if (n <= 0) break;
            char c = (char)buf[0];
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
            if (sb.Length > 8192) break;
        }
        return sb.ToString();
    }

    static async Task WriteResponse(NetworkStream s, int code, string json)
    {
        var status = code switch { 200 => "OK", 204 => "No Content", 404 => "Not Found", _ => "Error" };
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {code} {status}\r\n");
        sb.Append("Access-Control-Allow-Origin: *\r\n");
        sb.Append("Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n");
        sb.Append("Access-Control-Allow-Headers: Content-Type\r\n");
        sb.Append("Connection: close\r\n");
        if (code != 204)
        {
            var body = Encoding.UTF8.GetBytes(json);
            sb.Append("Content-Type: application/json\r\n");
            sb.Append($"Content-Length: {body.Length}\r\n\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            await s.WriteAsync(head, 0, head.Length);
            await s.WriteAsync(body, 0, body.Length);
        }
        else
        {
            sb.Append("\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            await s.WriteAsync(head, 0, head.Length);
        }
        await s.FlushAsync();
    }
}
