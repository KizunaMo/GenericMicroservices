namespace Demo.AuthService.Data;

public class User
{
    public int    Id           { get; set; }
    public string Username     { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;  // bcrypt 雜湊，不存明文
    public string Role         { get; set; } = "user";
}
