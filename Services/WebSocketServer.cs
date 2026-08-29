using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PentabServer.Models;

namespace PentabServer.Services
{
    public class WebSocketServer
    {
        private HttpListener? _httpListener;
        private CancellationTokenSource? _cts;
        private readonly InputInjector _inputInjector;

        public event Action<bool>? ServerStateChanged;
        public event Action<string>? ClientConnected;
        public event Action? ClientDisconnected;
        public event Action<PenData>? PenDataReceived;
        public event Action<string>? LogMessage;

        public bool IsRunning => _httpListener?.IsListening ?? false;
        public int Port { get; private set; } = 8765;

        public WebSocketServer(InputInjector inputInjector)
        {
            _inputInjector = inputInjector;
        }

        public void Start(int port = 8765)
        {
            if (IsRunning) Stop();

            Port = port;
            _cts = new CancellationTokenSource();

            try
            {
                _httpListener = new HttpListener();
                
                // Try registering wildcard prefix, fallback to specific IPs if restricted
                bool prefixAdded = false;
                try
                {
                    _httpListener.Prefixes.Add($"http://*:{port}/pentab/");
                    _httpListener.Start();
                    prefixAdded = true;
                }
                catch
                {
                    _httpListener.Close();
                    _httpListener = new HttpListener();
                }

                if (!prefixAdded)
                {
                    _httpListener.Prefixes.Add($"http://localhost:{port}/pentab/");
                    _httpListener.Prefixes.Add($"http://127.0.0.1:{port}/pentab/");

                    // Add local IP addresses
                    foreach (var ip in GetLocalIPAddresses())
                    {
                        try
                        {
                            _httpListener.Prefixes.Add($"http://{ip}:{port}/pentab/");
                        }
                        catch { }
                    }
                    _httpListener.Start();
                }

                LogMessage?.Invoke($"WebSocket server started on port {port}");
                ServerStateChanged?.Invoke(true);

                Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Failed to start server: {ex.Message}");
                Stop();
                throw;
            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            try
            {
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }

            _httpListener = null;
            _inputInjector.ResetButtons();
            ServerStateChanged?.Invoke(false);
            LogMessage?.Invoke("WebSocket server stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _httpListener != null && _httpListener.IsListening)
            {
                try
                {
                    var context = await _httpListener.GetContextAsync();
                    if (context.Request.IsWebSocketRequest)
                    {
                        _ = ProcessWebSocketClientAsync(context, ct);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task ProcessWebSocketClientAsync(HttpListenerContext context, CancellationToken ct)
        {
            HttpListenerWebSocketContext wsContext;
            try
            {
                wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"WebSocket handshake error: {ex.Message}");
                return;
            }

            var clientIp = context.Request.RemoteEndPoint?.ToString() ?? "Unknown";
            LogMessage?.Invoke($"Client connected from: {clientIp}");
            ClientConnected?.Invoke(clientIp);

            var socket = wsContext.WebSocket;
            var buffer = new byte[4096];

            try
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text && result.Count > 0)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        try
                        {
                            var penData = JsonSerializer.Deserialize<PenData>(json);
                            if (penData != null)
                            {
                                _inputInjector.Inject(penData);
                                PenDataReceived?.Invoke(penData);
                            }
                        }
                        catch (JsonException)
                        {
                            // Skip invalid packets
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke($"Client connection error: {ex.Message}");
            }
            finally
            {
                _inputInjector.ResetButtons();
                ClientDisconnected?.Invoke();
                LogMessage?.Invoke($"Client disconnected: {clientIp}");
                try
                {
                    socket.Dispose();
                }
                catch { }
            }
        }

        public static string[] GetLocalIPAddresses()
        {
            var ipList = new System.Collections.Generic.List<string>();
            try
            {
                foreach (var netInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (netInterface.OperationalStatus == OperationalStatus.Up &&
                        netInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        foreach (var addr in netInterface.GetIPProperties().UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                ipList.Add(addr.Address.ToString());
                            }
                        }
                    }
                }
            }
            catch { }
            return ipList.ToArray();
        }
    }
}
