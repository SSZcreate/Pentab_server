using System;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
            if (IsRunning) Stop();

            Port = port;
            _cts = new CancellationTokenSource();

            try
            {
                _tcpListener = new TcpListener(IPAddress.Any, port);
                _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                _tcpListener.Start(100);

                LogMessage?.Invoke($"WebSocket TCP server listening on 0.0.0.0:{port}");
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
            _activeClientCts?.Cancel();
            try
            {
                _tcpListener?.Stop();
            }
            catch { }

            _tcpListener = null;
            _inputInjector.ResetButtons();
            ServerStateChanged?.Invoke(false);
            LogMessage?.Invoke("WebSocket server stopped");
        }

        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _tcpListener != null)
            {
                try
                {
                    var tcpClient = await _tcpListener.AcceptTcpClientAsync(ct);
                    // Process client in a completely independent thread
                    _ = Task.Run(() => ProcessTcpClientAsync(tcpClient, ct), ct);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        LogMessage?.Invoke($"Accept error: {ex.Message}");
                    }
                    await Task.Delay(100, ct);
                }
            }
        }

        private async Task ProcessTcpClientAsync(TcpClient tcpClient, CancellationToken serverCt)
        {
            tcpClient.NoDelay = true;
            tcpClient.ReceiveTimeout = 0;
            tcpClient.SendTimeout = 5000;

            string endPoint = "Unknown";
            try
            {
                endPoint = tcpClient.Client.RemoteEndPoint?.ToString() ?? "Unknown";
            }
            catch { }

            LogMessage?.Invoke($"Incoming TCP connection from: {endPoint}");
            File.AppendAllText("server_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Incoming connection from {endPoint}\n");

            // Cancel any older active client to cleanly switch to the new connection
            var clientCts = CancellationTokenSource.CreateLinkedTokenSource(serverCt);
            var prevCts = Interlocked.Exchange(ref _activeClientCts, clientCts);
            prevCts?.Cancel();

            var ct = clientCts.Token;

            using (tcpClient)
            {
                var stream = tcpClient.GetStream();

                try
                {
                    // 1. Read HTTP handshake byte-by-byte up to \r\n\r\n
                    using var ms = new MemoryStream();
                    var singleByte = new byte[1];
                    int matchIndex = 0;
                    var matchPattern = new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' };

                    while (!ct.IsCancellationRequested)
                    {
                        int read = await stream.ReadAsync(singleByte, 0, 1, ct);
                        if (read <= 0)
                        {
                            File.AppendAllText("server_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Stream EOF during handshake from {endPoint}\n");
                            return;
                        }
                        ms.Write(singleByte, 0, 1);

                        if (singleByte[0] == matchPattern[matchIndex])
                        {
                            matchIndex++;
                            if (matchIndex == 4) break;
                        }
                        else
                        {
                            matchIndex = (singleByte[0] == matchPattern[0]) ? 1 : 0;
                        }

                        if (ms.Length > 8192) return;
                    }

                    string request = Encoding.UTF8.GetString(ms.ToArray());
                    string? clientKey = null;

                    using (var reader = new StringReader(request))
                    {
                        string? line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                            {
                                clientKey = line.Substring("Sec-WebSocket-Key:".Length).Trim();
                                break;
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(clientKey))
                    {
                        var badResponse = Encoding.UTF8.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\n");
                        await stream.WriteAsync(badResponse, ct);
                        return;
                    }

                    var acceptKey = Convert.ToBase64String(
                        SHA1.HashData(Encoding.UTF8.GetBytes(clientKey + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11"))
                    );

                    var responseHeader = "HTTP/1.1 101 Switching Protocols\r\n" +
                                         "Upgrade: websocket\r\n" +
                                         "Connection: Upgrade\r\n" +
                                         $"Sec-WebSocket-Accept: {acceptKey}\r\n\r\n";

                    var responseBytes = Encoding.UTF8.GetBytes(responseHeader);
                    await stream.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
                    await stream.FlushAsync(ct);

                    File.AppendAllText("server_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Handshake complete for {endPoint}\n");
                    LogMessage?.Invoke($"WebSocket connected: {endPoint}");
                    ClientConnected?.Invoke(endPoint);

                    // 2. RFC 6455 WebSocket Frame Loop
                    while (!ct.IsCancellationRequested)
                    {
                        var header = new byte[2];
                        if (!await ReadExactAsync(stream, header, 0, 2, ct)) break;

                        byte b0 = header[0];
                        byte b1 = header[1];

                        int opcode = b0 & 0x0F;
                        bool isMasked = (b1 & 0x80) != 0;
                        long payloadLen = b1 & 0x7F;

                        if (payloadLen == 126)
                        {
                            var ext = new byte[2];
                            if (!await ReadExactAsync(stream, ext, 0, 2, ct)) break;
                            payloadLen = (ext[0] << 8) | ext[1];
                        }
                        else if (payloadLen == 127)
                        {
                            var ext = new byte[8];
                            if (!await ReadExactAsync(stream, ext, 0, 8, ct)) break;
                            payloadLen = 0;
                            for (int i = 0; i < 8; i++) payloadLen = (payloadLen << 8) | ext[i];
                        }

                        byte[]? mask = null;
                        if (isMasked)
                        {
                            mask = new byte[4];
                            if (!await ReadExactAsync(stream, mask, 0, 4, ct)) break;
                        }

                        if (payloadLen > 1024 * 1024) break;

                        var payload = new byte[payloadLen];
                        if (payloadLen > 0)
                        {
                            if (!await ReadExactAsync(stream, payload, 0, (int)payloadLen, ct)) break;
                            if (isMasked && mask != null)
                            {
                                for (int i = 0; i < payload.Length; i++)
                                {
                                    payload[i] = (byte)(payload[i] ^ mask[i % 4]);
                                }
                            }
                        }

                        if (opcode == 0x8) // Close
                        {
                            var closeResp = new byte[] { 0x88, 0x00 };
                            await stream.WriteAsync(closeResp, 0, closeResp.Length, ct);
                            break;
                        }
                        else if (opcode == 0x9) // Ping
                        {
                            var pongHeader = new byte[] { (byte)0x8A, (byte)payload.Length };
                            await stream.WriteAsync(pongHeader, 0, pongHeader.Length, ct);
                            if (payload.Length > 0)
                            {
                                await stream.WriteAsync(payload, 0, payload.Length, ct);
                            }
                            await stream.FlushAsync(ct);
                        }
                        else if (opcode == 0x1) // Text
                        {
                            var json = Encoding.UTF8.GetString(payload);
                            try
                            {
                                var penData = JsonSerializer.Deserialize<PenData>(json);
                                if (penData != null)
                                {
                                    File.AppendAllText("server_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Event: {penData.Action} ({penData.X:F3}, {penData.Y:F3}) p={penData.Pressure:F2} tool={penData.ToolType}\n");
                                    _inputInjector.Inject(penData);
                                    PenDataReceived?.Invoke(penData);
                                }
                            }
                            catch (JsonException) { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    File.AppendAllText("server_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Exception: {ex.Message} for {endPoint}\n");
                    if (!ct.IsCancellationRequested)
                    {
                        LogMessage?.Invoke($"Connection error: {ex.Message}");
                    }
                }
                finally
                {
                    File.AppendAllText("server_debug.log", $"[{DateTime.Now:HH:mm:ss.fff}] Client disconnected: {endPoint}\n");
                    try
                    {
                        stream?.Close();
                        tcpClient?.Close();
                    }
                    catch { }
                    _inputInjector.ResetButtons();
                    ClientDisconnected?.Invoke();
                    LogMessage?.Invoke($"Client disconnected: {endPoint}");
                }
            }
        }

        private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
        {
            int totalRead = 0;
            while (totalRead < count)
            {
                int read = await stream.ReadAsync(buffer, offset + totalRead, count - totalRead, ct);
                if (read <= 0) return false;
                totalRead += read;
            }
            return true;
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
