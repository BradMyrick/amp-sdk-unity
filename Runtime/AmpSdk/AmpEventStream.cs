// AMP Unity SDK — live event stream over ClientWebSocket with keepalive.

using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Amp.Sdk
{
    /// <summary>
    /// A live connection to the matchmaker event stream. onEvent receives
    /// raw JSON ({"type":"…","data":{…}}) on a background thread.
    /// </summary>
    public class AmpEventStream : IDisposable
    {
        private readonly ClientWebSocket _ws;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        private AmpEventStream(ClientWebSocket ws) => _ws = ws;

        public static async Task<AmpEventStream> ConnectAsync(string server, string token)
        {
            var url = server.TrimEnd('/')
                .Replace("https://", "wss://").Replace("http://", "ws://")
                + "/v1/ws?token=" + Uri.EscapeDataString(token);

            var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(url), CancellationToken.None);
            var stream = new AmpEventStream(ws);
            return stream;
        }

        /// <summary>Read events until the stream closes. Marshaling to the main thread is the caller's job (AmpManager does it).</summary>
        public async Task ListenAsync(Action<string> onEvent)
        {
            var buffer = new byte[16 * 1024];
            var sb = new StringBuilder();
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(
                        new ArraySegment<byte>(buffer), _cts.Token);
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                    break;
                }
                if (result.MessageType == WebSocketMessageType.Text && sb.Length > 0)
                {
                    var json = sb.ToString();
                    // Server ping → pong ({"type":"ping"} … we also use it as keepalive)
                    if (json.Contains("\"ping\""))
                    {
                        await _ws.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"ping\"}")),
                            WebSocketMessageType.Text, true, _cts.Token);
                        continue;
                    }
                    onEvent(json);
                }
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            try { _ws.Dispose(); } catch { /* closing */ }
        }
    }
}
