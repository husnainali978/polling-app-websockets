using Microsoft.EntityFrameworkCore;
using PollingApp.WebSockets.Data;
using PollingApp.WebSockets.Dtos;

namespace PollingApp.WebSockets.Services;

/// <summary>
/// Read-side queries shared by the HTTP endpoints and the WebSocket endpoint
/// (both need to build the same "current tally" shape: one right after a vote
/// is cast, the other right after a socket connects).
/// </summary>
public class PollReadService
{
    private readonly PollDbContext _db;

    public PollReadService(PollDbContext db)
    {
        _db = db;
    }

    public async Task<List<PollSummaryResponse>> GetAllSummariesAsync(CancellationToken cancellationToken = default)
    {
        return await _db.Polls
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PollSummaryResponse(
                p.Id,
                p.Question,
                p.CreatedAt,
                p.Options.Count,
                p.Options.SelectMany(o => o.Votes).Count()))
            .ToListAsync(cancellationToken);
    }

    public async Task<PollResponse?> GetPollAsync(Guid pollId, CancellationToken cancellationToken = default)
    {
        var poll = await _db.Polls
            .Include(p => p.Options)
                .ThenInclude(o => o.Votes)
            .AsNoTracking()
            // Two nested collections (Options, then each option's Votes) in one
            // query risk a cartesian-product join; splitting into separate
            // SQL queries avoids that and the associated EF Core warning.
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == pollId, cancellationToken);

        return poll is null ? null : ToResponse(poll.Id, poll.Question, poll.CreatedAt, poll.Options);
    }

    private static PollResponse ToResponse(Guid id, string question, DateTime createdAt, IEnumerable<Models.PollOption> options)
    {
        var optionResponses = options
            .Select(o => new PollOptionResponse(o.Id, o.Text, o.Votes.Count))
            .ToList();

        return new PollResponse(id, question, createdAt, optionResponses.Sum(o => o.VoteCount), optionResponses);
    }
}
