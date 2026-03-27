namespace Demo.TcpService.Protocol;

public class Packet
{
    public MessageCategory Category { get; init; }
    public int             SubType  { get; init; }
    public string          Data     { get; init; } = string.Empty;
}
