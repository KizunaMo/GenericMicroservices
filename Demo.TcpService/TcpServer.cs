using System.Net;
using System.Net.Sockets;
using System.Text;
using Demo.TcpService.Protocol;
using Demo.TcpService.Protocol.Handlers;

namespace Demo.TcpService;

public class TcpServer
{
    private readonly TcpListener _listener;

    // 根據 (Category, SubType) 找對應的 Handler
    private readonly Dictionary<(MessageCategory, int), IMessageHandler> _handlers = new()
    {
        { (MessageCategory.Item,   (int)ItemSubType.Query),        new ItemQueryHandler()      },
        { (MessageCategory.Item,   (int)ItemSubType.Create),       new ItemCreateHandler()     },
        { (MessageCategory.System, (int)SystemSubType.Heartbeat),  new SystemHeartbeatHandler()},
    };

    public TcpServer(int port)
    {
        _listener = new TcpListener(IPAddress.Any, port);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener.Start();
        Console.WriteLine($"[Server] Listening on port {((IPEndPoint)_listener.LocalEndpoint).Port}");

        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(ct);
            Console.WriteLine($"[Server] Client connected: {client.Client.RemoteEndPoint}");
            _ = HandleClientAsync(client, ct);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        await using var stream = client.GetStream();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 1. 讀 Category（4 bytes）
                var categoryBuffer = new byte[4];
                if (await ReadExactAsync(stream, categoryBuffer, ct) == 0) break;
                var category = (MessageCategory)BitConverter.ToInt32(categoryBuffer, 0);

                // 2. 讀 SubType（4 bytes）
                var subTypeBuffer = new byte[4];
                await ReadExactAsync(stream, subTypeBuffer, ct);
                var subType = BitConverter.ToInt32(subTypeBuffer, 0);

                // 3. 讀 Length（4 bytes）
                var lengthBuffer = new byte[4];
                await ReadExactAsync(stream, lengthBuffer, ct);
                var dataLength = BitConverter.ToInt32(lengthBuffer, 0);

                // 4. 讀 Data（dataLength bytes）
                var dataBuffer = new byte[dataLength];
                if (dataLength > 0)
                    await ReadExactAsync(stream, dataBuffer, ct);
                var data = Encoding.UTF8.GetString(dataBuffer);

                Console.WriteLine($"[Server] Received → Category: {category}, SubType: {subType}, Data: \"{data}\"");

                // 5. 分派到對應 Handler
                var key = (category, subType);
                if (_handlers.TryGetValue(key, out var handler))
                {
                    var response = await handler.HandleAsync(data);
                    await SendMessageAsync(stream, response, ct);
                }
                else
                {
                    await SendMessageAsync(stream, $"[Error] Unknown Category={category}, SubType={subType}", ct);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // Client 斷線或服務停止，正常情況
        }
        finally
        {
            Console.WriteLine($"[Server] Client disconnected: {client.Client.RemoteEndPoint}");
            client.Close();
        }
    }

    private static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead, ct);
            if (read == 0) return 0;
            totalRead += read;
        }
        return totalRead;
    }

    private static async Task SendMessageAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        var data = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(data.Length);
        await stream.WriteAsync(lengthBytes, ct);
        await stream.WriteAsync(data, ct);
    }

    public void Stop() => _listener.Stop();
}
