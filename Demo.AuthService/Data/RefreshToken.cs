namespace Demo.AuthService.Data;

public class RefreshToken
{
    public int      Id        { get; set; }
    public string   Token     { get; set; } = string.Empty;  // 隨機字串，非 JWT
    public int      UserId    { get; set; }
    public User     User      { get; set; } = null!;
    public DateTime ExpiresAt { get; set; }
    public bool     IsRevoked { get; set; } = false;
}
