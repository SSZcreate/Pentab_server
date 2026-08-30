using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PentabServer.Models;

namespace PentabServer.Services
{
    public class WebSocketServer
    {
        private TcpListener? _tcpListener;
        private CancellationTokenSource? _cts;
        private readonly InputInjector _inputInjector;
        private CancellationTokenSource? _activeClientCts;
        private TcpClient? _currentTcpClient;

        public event Action<bool>? ServerStateChanged;
        public event Action<string>? ClientConnected;
        public event Action? ClientDisconnected;
        public event Action<PenData>? PenDataReceived;
        public event Action<string>? LogMessage;

        public bool IsRunning => _tcpListener != null;
        public int Port { get; private set; } = 8765;

        public WebSocketServer(InputInjector inputInjector)
        {
            _inputInjector = inputInjector;
        }

        public void Start(int port = 8765)
        {
            if (IsRunning) return;

            Port = port;
            _cts = new CancellationTokenSource();

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _tcpListener.Start();

                ServerStateChanged?.Invoke(true);
                LogMessage?.Invoke($"Server started listening on 0.0.0.0:{port}");

                Task.Run(() => AcceptLoopAsync(_cts.Token));
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
            _activeClientCts?.Cancel();
            try { _currentTcpClient?.Close(); } catch { }

            try
            {
                _tcpListener?.Stop();
            }
            catch { }

            _tcpListener = null;
            _inputInjector.ResetButtons();
            ServerStateChanged?.Invoke(false);
            LogMessage?.Invoke("Server stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync(ct);
                    _ = Task.Run(() => ProcessTcpClientAsync(tcpClient, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogMessage?.Invoke($"Accept error: {ex.Message}");
                }
            }
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient, CancellationToken serverCt)
        {
            tcpClient.NoDelay = true;
            tcpClient.ReceiveTimeout = 10000;
            tcpClient.SendTimeout = 5000;

            string endPoint = "Unknown";
            try
            {
                endPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            }
            catch { }

            LogMessage?.Invoke($"Incoming TCP connection from: {endPoint}");
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Incoming connection from {endPoint}\n");

            // Cancel and close older client
            var clientCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            var prevCts = Interlocked.Exchange(ref _activeClientCts, clientCts);
            prevCts?.Cancel();

            var prevClient = Interlocked.Exchange(ref _currentTcpClient, tcpClient);
            try { prevClient?.Close(); } catch { }

            var ct = clientCts.Token;

            using (tcpClient)
            using (var stream = tcpClient.GetStream())
            {
                try
                {
                    // Read HTTP upgrade request
                    var headerBytes = new byte[4096];
                    int totalRead = 0;
                    while (totalRead < headerBytes.Length && !ct.IsCancellationRequested)
                    {
                        int read = await stream.ReadAsync(headerBytes, totalRead, 1, ct);
                        if (read <= 0) return;
                        totalRead += read;
                        if (totalRead >= 4 &&
                            headerBytes[totalRead - 4] == '\r' &&
                            headerBytes[totalRead - 3] == '\n' &&
                            headerBytes[totalRead - 2] == '\r' &&
                            headerBytes[totalRead - 1] == '\n')
                        {
                            break;
                        }
                    }

                    string requestStr = Encoding.UTF8.GetString(headerBytes, 0, totalRead);
                    string? secKey = null;

                    var lines = requestStr.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    foreach (var line in lines)
                    {
                        if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                        {
                            secKey = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                            break;
                        }
                    }

                    if (string.IsNullOrEmpty(secKey))
                    {
                        File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Missing Sec-WebSocket-Key\n");
                        return;
                    }

                    // Compute WebSocket accept hash
                    string acceptKey;
                    using (var sha1 = SHA1.Create())
                    {
                        byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(secKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"));
                        acceptKey = Convert.ToBase64String(hashBytes);
                    }

                    // Send 101 Switching Protocols response
                    string response =
                        "HTTP/1.1 101 Switching Protocols\r\n" +
                        "Upgrade: websocket\r\n" +
                        "Connection: Upgrade\r\n" +
                        $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

                    byte[] responseBytes = Encoding.UTF8.GetBytes(response);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                    await stream.FlushAsync(ct);

                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Handshake complete for {endPoint}\n");
                    LogMessage?.Invoke($"WebSocket connected: {endPoint}");
                    ClientConnected?.Invoke(endPoint);

                    // Frame loop
                    while (!ct.IsCancellationRequested && tcpClient.Connected)
                    {
                        byte[] head = new byte[2];
                        if (!await ReadExactAsync(stream, head, 0, 2, ct)) break;

                        byte b1 = head[0];
                        byte b2 = head[1];

                        int opcode = b1 & 0x0F;
                        bool mask = (b2 & 0x80) != 0;
                        int payloadLen = b2 & 0x7F;

                        if (opcode == 8) // Close
                        {
                            break;
                        }

                        if (payloadLen == 126)
                        {
                            byte[] ext = new byte[2];
                            if (!await ReadExactAsync(stream, ext, 0, 2, ct)) break;
                            payloadLen = (ext[0] << 8) | ext[1];
                        }
                        else if (payloadLen == 127)
                        {
                            byte[] ext = new byte[8];
                            if (!await ReadExactAsync(stream, ext, 0, 8, ct)) break;
                            payloadLen = (int)((((ulong)ext[0]) << 56) | (((ulong)ext[1]) << 48) |
                                               (((ulong)ext[2]) << 40) | (((ulong)ext[3]) << 32) |
                                               (((ulong)ext[4]) << 24) | (((ulong)ext[5]) << 16) |
                                               (((ulong)ext[6]) << 8) | (ulong)ext[7]);
                        }

                        byte[] maskingKey = new byte[4];
                        if (mask)
                        {
                            if (!await ReadExactAsync(stream, maskingKey, 0, 4, ct)) break;
                        }

                        byte[] payload = new byte[payloadLen];
                        if (payloadLen > 0)
                        {
                            if (!await ReadExactAsync(stream, payload, 0, payloadLen, ct)) break;
                        }

                        if (mask)
                        {
                            for (int i = 0; i < payloadLen; i++)
                            {
                                payload[i] = (byte)(payload[i] ^ maskingKey[i % 4]);
                            }
                        }

                        if (opcode == 9) // Ping -> Send Pong
                        {
                            byte[] pong = new byte[2 + payloadLen];
                            pong[0] = 0x8A; // Pong opcode 10
                            pong[1] = (byte)payloadLen;
                            Buffer.BlockCopy(payload, 0, pong, 2, payloadLen);
                            await stream.WriteAsync(pong, 0, pong.Length, ct);
                            await stream.FlushAsync(ct);
                        }
                        else if (opcode == 1 || opcode == 2) // Text or Binary
                        {
                            string json = Encoding.UTF8.GetString(payload);
                            try
                            {
                                var penData = JsonSerializer.Deserialize<PenData>(json);
                                if (penData != null)
                                {
                                    _inputInjector.Inject(penData);
                                    PenDataReceived?.Invoke(penData);
                                }
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LogMessage?.Invoke($"Client error: {ex.Message}");
                }
                finally
                {
                    _inputInjector.ResetButtons();
                    ClientDisconnected?.Invoke();
                    LogMessage?.Invoke($"WebSocket disconnected: {endPoint}");
                    File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "server_debug.log"), $"[{DateTime.Now:HH:mm:ss.fff}] Client disconnected: {endPoint}\n");
                }
            }
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int total = 0;
            while (total < count)
            {
                int read = await stream.ReadAsync(buffer, offset + total, count - total, ct);
                if (read <= 0) return false;
                total += read;
            }
            return true;
        }

        public static string[] GetLocalIPAddresses()
        {
            var ips = new System.Collections.Generic.List<string>();
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                    {
                        ips.Add(ip.ToString());
                    }
                }
            }
            catch { }
            return ips.ToArray();
        }
    }
}
