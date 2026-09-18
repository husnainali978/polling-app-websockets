namespace PollingApp.WebSockets.Models;

/// <summary>
/// One selectable answer belonging to a poll.
/// </summary>
public class PollOption
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PollId { get; set; }

    public Poll? Poll { get; set; }

    public string Text { get; set; } = string.Empty;

    public List<Vote> Votes { get; set; } = new();
}
