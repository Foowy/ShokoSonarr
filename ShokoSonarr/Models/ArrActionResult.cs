namespace ShokoSonarr.Models;

/// <summary>Typed result envelope for *arr-family API calls — never throws on HTTP/connectivity failure, callers check <see cref="Success"/>.</summary>
public class ArrActionResult<T>
{
    /// <summary>Whether the call succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>The result payload, if <see cref="Success"/> is true.</summary>
    public T? Data { get; init; }

    /// <summary>A human-readable error message, if <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Builds a successful result.</summary>
    public static ArrActionResult<T> Ok(T data) => new() { Success = true, Data = data };

    /// <summary>Builds a failed result.</summary>
    public static ArrActionResult<T> Fail(string message) => new() { Success = false, ErrorMessage = message };
}
