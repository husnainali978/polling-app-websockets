namespace PollingApp.WebSockets.Dtos;

/// <summary>Request body for creating a new poll.</summary>
/// <param name="Question">The question being asked.</param>
/// <param name="Options">Two or more answer choices.</param>
public record CreatePollRequest(string Question, List<string> Options);

/// <summary>Request body for casting a vote.</summary>
/// <param name="OptionId">The id of the option being voted for.</param>
public record VoteRequest(Guid OptionId);

/// <summary>A single option and its current vote count.</summary>
public record PollOptionResponse(Guid Id, string Text, int VoteCount);

/// <summary>Full poll detail, including per-option tallies.</summary>
public record PollResponse(Guid Id, string Question, DateTime CreatedAt, int TotalVotes, List<PollOptionResponse> Options);

/// <summary>Lightweight summary used for the poll list view.</summary>
public record PollSummaryResponse(Guid Id, string Question, DateTime CreatedAt, int OptionCount, int TotalVotes);

/// <summary>
/// Envelope pushed to WebSocket clients. "snapshot" is sent once, right after a
/// client connects; "update" is broadcast to every connected client whenever a
/// new vote is recorded for that poll.
/// </summary>
public record PollSocketMessage(string Type, PollResponse Poll);
