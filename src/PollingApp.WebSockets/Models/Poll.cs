namespace PollingApp.WebSockets.Models;

/// <summary>
/// A poll/survey with a question and a fixed set of selectable options.
/// </summary>
public class Poll
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Question { get; set; } = string.Empty;

    // Stored as DateTime (UTC) rather than DateTimeOffset: SQLite's EF Core
    // provider cannot translate ORDER BY over DateTimeOffset columns into SQL.
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<PollOption> Options { get; set; } = new();
}
