using System.Net.Sockets;
using System.Text;

namespace Demo.TcpTestConsole;

public class TcpClientHelper : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private TcpClientHelper(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<TcpClientHelper> ConnectAsync(string host, int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port);
        Console.WriteLine($"[Client] Connected to {host}:{port}");
        return new TcpClientHelper(client);
    }

    // 送出封包：[ Category ][ SubType ][ Length ][ Data ]
    public async Task SendAsync(int category, int subType, string data)
    {
        var dataBytes   = Encoding.UTF8.GetBytes(data);
        var categoryBytes = BitConverter.GetBytes(category);
        var subTypeBytes  = BitConverter.GetBytes(subType);
        var lengthBytes   = BitConverter.GetBytes(dataBytes.Length);

        await _stream.WriteAsync(categoryBytes);
        await _stream.WriteAsync(subTypeBytes);
        await _stream.WriteAsync(lengthBytes);
        if (dataBytes.Length > 0)
            await _stream.WriteAsync(dataBytes);

        Console.WriteLine($"[Client] Sent → Category: {category}, SubType: {subType}, Data: \"{data}\"");
    }

    // 接收回應（Server 回應只有 Length + Data）
    public async Task<string> ReceiveAsync()
    {
        var lengthBuffer = new byte[4];
        await ReadExactAsync(lengthBuffer);

        var dataLength = BitConverter.ToInt32(lengthBuffer, 0);
        var dataBuffer = new byte[dataLength];
        await ReadExactAsync(dataBuffer);

        return Encoding.UTF8.GetString(dataBuffer);
    }

    private async Task ReadExactAsync(byte[] buffer)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0) throw new IOException("Connection closed by server.");
            totalRead += read;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _client.Close();
        Console.WriteLine("[Client] Disconnected.");
    }
}
