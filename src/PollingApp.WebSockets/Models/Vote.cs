namespace PollingApp.WebSockets.Models;

/// <summary>
/// A single cast vote for a poll option. Kept as its own row (rather than just
/// an incrementing counter on PollOption) so the schema demonstrates a real
/// Poll -&gt; PollOption -&gt; Vote relational model and leaves room for future
/// features (per-voter dedupe, timeline charts, etc.) without a schema change.
/// </summary>
public class Vote
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PollOptionId { get; set; }

    public PollOption? PollOption { get; set; }

    public DateTime VotedAt { get; set; } = DateTime.UtcNow;
}
