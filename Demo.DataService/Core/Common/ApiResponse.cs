namespace Demo.DataService.Core.Common;

public class ApiResponse<T>
{
    public bool Success { get; init; }
    public T? Data { get; init; }
    public string? Error { get; init; }

    public static ApiResponse<T> Ok(T data)
        => new() { Success = true, Data = data };

    public static ApiResponse<T> Fail(string error)
        => new() { Success = false, Error = error };
}
