using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Demo.LockstepService;

/// <summary>
/// Lockstep TCP Server（port 5500）。
///
/// 協議 — Client → Server：
///   [Category  : 4B LE int32] = 3
///   [SubType   : 4B LE int32] = 1
///   [DataLength: 4B LE int32]
///   [Data      : N bytes    ] = LockstepInputPacket
///     └─ [FrameNumber : 4B LE int32]
///        [PlayerIdLen : 4B LE int32]
///        [PlayerId    : N bytes UTF-8]
///        [InputLen    : 4B LE int32]
///        [InputBytes  : N bytes]
///
/// 協議 — Server → Client（廣播）：
///   [Length : 4B LE int32]
///   [Message: N bytes UTF-8 JSON]
///     └─ { "frameNumber": N, "inputs": [{ "playerId": "A", "input": "<Base64>" }] }
/// </summary>
public class LockstepServer
{
    readonly TcpListener _listener;
    readonly List<string> _playerIds;

    // 所有連線的 stream，用來廣播
    readonly ConcurrentDictionary<string, NetworkStream> _connections = new();

    // frameNumber → (playerId → inputBytes)
    readonly ConcurrentDictionary<int, ConcurrentDictionary<string, byte[]>> _frameBuffer = new();

    public LockstepServer(int port, List<string> playerIds)
    {
        _listener  = new TcpListener(IPAddress.Any, port);
        _playerIds = playerIds;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _listener.Start();
        Console.WriteLine($"[LockstepServer] Listening on port 5500, expecting players: {string.Join(", ", _playerIds)}");

        while (!ct.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(ct);
            Console.WriteLine($"[LockstepServer] Client connected: {client.Client.RemoteEndPoint}");
            _ = HandleClientAsync(client, ct);
        }
    }

    async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        await using var stream = client.GetStream();
        string? connectedPlayerId = null;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // 1. 讀 Header（Category + SubType + DataLength = 12 bytes）
                var header = new byte[12];
                if (await ReadExactAsync(stream, header, ct) == 0) break;

                var category   = BitConverter.ToInt32(header, 0);
                var subType    = BitConverter.ToInt32(header, 4);
                var dataLength = BitConverter.ToInt32(header, 8);

                // 2. 讀 Data
                var data = new byte[dataLength];
                if (dataLength > 0)
                    await ReadExactAsync(stream, data, ct);

                Console.WriteLine($"[LockstepServer] Received → Category: {category}, SubType: {subType}, DataLen: {dataLength}");

                // 3. 分派 Category=3 的封包
                if (category == 3 && subType == 0)
                {
                    // Join：登記玩家連線
                    var playerId = Encoding.UTF8.GetString(data);
                    if (!_connections.ContainsKey(playerId))
                    {
                        _connections[playerId] = stream;
                        connectedPlayerId = playerId;
                        Console.WriteLine($"[LockstepServer] Player joined: {playerId} ({_connections.Count}/{_playerIds.Count})");

                        // 所有玩家都到齊 → 廣播 GameStart
                        if (_connections.Count == _playerIds.Count)
                            await BroadcastGameStartAsync(ct);
                    }
                }
                else if (category == 3 && subType == 1)
                {
                    // SendInput：收幀 input
                    var (frameNumber, playerId, inputBytes) = ParseInputPacket(data);
                    await ReceiveInputAsync(playerId, frameNumber, inputBytes, ct);
                }
                else
                {
                    await SendMessageAsync(stream, $"[Error] Unknown Category={category}, SubType={subType}", ct);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
            // 正常斷線
        }
        finally
        {
            if (connectedPlayerId != null)
                _connections.TryRemove(connectedPlayerId, out _);

            Console.WriteLine($"[LockstepServer] Client disconnected: {client.Client.RemoteEndPoint}");
            client.Close();
        }
    }

    async Task ReceiveInputAsync(string playerId, int frameNumber, byte[] inputBytes, CancellationToken ct)
    {
        var frameInputs = _frameBuffer.GetOrAdd(frameNumber, _ => new ConcurrentDictionary<string, byte[]>());
        frameInputs[playerId] = inputBytes;

        Console.WriteLine($"[LockstepServer] Frame {frameNumber}: received from {playerId} ({frameInputs.Count}/{_playerIds.Count})");

        // 齊了就廣播（TryRemove 確保只有一個 task 能廣播）
        if (frameInputs.Count >= _playerIds.Count && _frameBuffer.TryRemove(frameNumber, out _))
        {
            await BroadcastFrameAsync(frameNumber, frameInputs, ct);
        }
    }

    async Task BroadcastGameStartAsync(CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { type = "GameStart" });
        Console.WriteLine($"[LockstepServer] All players ready, broadcasting GameStart.");
        foreach (var (_, stream) in _connections)
        {
            try { await SendMessageAsync(stream, json, ct); }
            catch (Exception ex) { Console.WriteLine($"[LockstepServer] GameStart broadcast error: {ex.Message}"); }
        }
    }

    async Task BroadcastFrameAsync(int frameNumber, ConcurrentDictionary<string, byte[]> inputs, CancellationToken ct)
    {
        // 建立 JSON
        var inputList = new List<object>();
        foreach (var (playerId, inputBytes) in inputs)
        {
            inputList.Add(new
            {
                playerId,
                input = Convert.ToBase64String(inputBytes)
            });
        }

        var json = JsonSerializer.Serialize(new
        {
            frameNumber,
            inputs = inputList
        });

        Console.WriteLine($"[LockstepServer] Broadcasting frame {frameNumber}: {json}");

        // 廣播給所有連線中的玩家
        foreach (var (_, stream) in _connections)
        {
            try
            {
                await SendMessageAsync(stream, json, ct);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LockstepServer] Broadcast error: {ex.Message}");
            }
        }
    }

    // ── 封包解析 ──────────────────────────────────────────────────────────────

    static (int frameNumber, string playerId, byte[] inputBytes) ParseInputPacket(byte[] data)
    {
        var offset = 0;

        var frameNumber  = BitConverter.ToInt32(data, offset); offset += 4;
        var playerIdLen  = BitConverter.ToInt32(data, offset); offset += 4;
        var playerId     = Encoding.UTF8.GetString(data, offset, playerIdLen); offset += playerIdLen;
        var inputLen     = BitConverter.ToInt32(data, offset); offset += 4;
        var inputBytes   = new byte[inputLen];

        if (inputLen > 0)
            Buffer.BlockCopy(data, offset, inputBytes, 0, inputLen);

        return (frameNumber, playerId, inputBytes);
    }

    // ── 工具方法 ──────────────────────────────────────────────────────────────

    static async Task<int> ReadExactAsync(NetworkStream stream, byte[] buffer, CancellationToken ct)
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

    static async Task SendMessageAsync(NetworkStream stream, string message, CancellationToken ct)
    {
        var data        = Encoding.UTF8.GetBytes(message);
        var lengthBytes = BitConverter.GetBytes(data.Length);
        await stream.WriteAsync(lengthBytes, ct);
        await stream.WriteAsync(data, ct);
    }

    public void Stop() => _listener.Stop();
}
