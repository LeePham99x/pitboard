using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pitboard.Windows.Tests;

// A web site on this PC's loopback for the WebView2 tests, so nothing reaches the network: every path is a page, /set
// sets a cookie, and it says which paths it was asked for.
internal sealed class StandInSite : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stop = new();

    public StandInSite()
    {
        _listener.Start();
        _ = Serve();
    }

    public ConcurrentQueue<string> Asked { get; } = new();

    public string Address(string path) => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}";

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }

    private async Task Serve()
    {
        while (true)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch (Exception stopped) when (stopped is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return;
            }

            _ = Answer(client);
        }
    }

    private async Task Answer(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
                var path = (await reader.ReadLineAsync())?.Split(' ') is [_, var asked, ..] ? asked : "/";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                }

                Asked.Enqueue(path);
                var cookie = path == "/set" ? "Set-Cookie: who=here; Path=/\r\n" : "";
                var body = "<!doctype html><html><head><title>stand-in</title></head><body><p>A stand-in page.</p></body></html>";
                var answer =
                    $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(body)}\r\n"
                    + $"{cookie}Connection: close\r\n\r\n{body}";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(answer));
            }
            catch (IOException)
            {
            }
        }
    }
}
