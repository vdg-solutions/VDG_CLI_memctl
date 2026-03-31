namespace Memctl.CoreAbstractions.Entities;

public sealed class MemctlOutcome
{
    public bool    Success { get; init; }
    public string  Action  { get; init; } = "";
    public string  Message { get; init; } = "";
    public object? Data    { get; init; }

    public static MemctlOutcome Ok(string action, string message, object? data = null) =>
        new() { Success = true, Action = action, Message = message, Data = data };

    public static MemctlOutcome Fail(string action, string message) =>
        new() { Success = false, Action = action, Message = message };
}
