namespace Demo.Contracts;

// 事件合約：Item 被建立時發布這個事件
// Producer 和 Consumer 都 reference 這個定義
public record ItemCreated
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
