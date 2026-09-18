using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PollingApp.WebSockets.Data;
using PollingApp.WebSockets.Dtos;
using PollingApp.WebSockets.Models;
using PollingApp.WebSockets.Services;

namespace PollingApp.WebSockets.Endpoints;

public static class PollEndpoints
{
    public static void MapPollEndpoints(this IEndpointRouteBuilder app)
    {
        var polls = app.MapGroup("/api/polls").WithTags("Polls");

        // Create a poll with 2+ options.
        polls.MapPost("/", async (CreatePollRequest request, PollDbContext db) =>
        {
            var question = request.Question?.Trim() ?? string.Empty;
            var options = (request.Options ?? new List<string>())
                .Select(o => o?.Trim() ?? string.Empty)
                .Where(o => o.Length > 0)
                .ToList();

            if (question.Length == 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["question"] = new[] { "A poll question is required." }
                });
            }

            if (options.Count < 2)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["options"] = new[] { "A poll needs at least two options." }
                });
            }

            var poll = new Poll
            {
                Question = question,
                Options = options.Select(text => new PollOption { Text = text }).ToList()
            };

            db.Polls.Add(poll);
            await db.SaveChangesAsync();

            var response = new PollResponse(
                poll.Id,
                poll.Question,
                poll.CreatedAt,
                0,
                poll.Options.Select(o => new PollOptionResponse(o.Id, o.Text, 0)).ToList());

            return Results.Created($"/api/polls/{poll.Id}", response);
        });

        // List all polls (summary view: no per-option breakdown, just totals).
        polls.MapGet("/", async (PollReadService reads) =>
        {
            var summaries = await reads.GetAllSummariesAsync();
            return Results.Ok(summaries);
        });

        // Fetch a single poll with its current per-option vote tallies.
        polls.MapGet("/{pollId:guid}", async (Guid pollId, PollReadService reads) =>
        {
            var poll = await reads.GetPollAsync(pollId);
            return poll is null ? Results.NotFound() : Results.Ok(poll);
        });

        // Cast a vote for one option, persist it, then broadcast the new tallies
        // to every WebSocket client currently subscribed to this poll.
        polls.MapPost("/{pollId:guid}/vote", async (
            Guid pollId,
            VoteRequest request,
            PollDbContext db,
            PollReadService reads,
            Services.PollConnectionManager connections) =>
        {
            var option = await db.PollOptions
                .FirstOrDefaultAsync(o => o.Id == request.OptionId && o.PollId == pollId);

            if (option is null)
            {
                return Results.NotFound(new { message = "Poll or option not found." });
            }

            db.Votes.Add(new Vote { PollOptionId = option.Id });
            await db.SaveChangesAsync();

            var updated = await reads.GetPollAsync(pollId);
            if (updated is not null)
            {
                var message = new PollSocketMessage("update", updated);
                var json = JsonSerializer.Serialize(message, PollJson.Options);
                await connections.BroadcastAsync(pollId.ToString(), json);
            }

            return Results.Ok(updated);
        });
    }
}
