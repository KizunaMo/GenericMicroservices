namespace Demo.TcpService.Protocol;

public interface IMessageHandler
{
    Task<string> HandleAsync(string data);
}
