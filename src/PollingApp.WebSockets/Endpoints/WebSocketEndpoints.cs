using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using PollingApp.WebSockets.Dtos;
using PollingApp.WebSockets.Services;

namespace PollingApp.WebSockets.Endpoints;

public static class WebSocketEndpoints
{
    /// <summary>
    /// Raw WebSocket endpoint for live poll results. This is deliberately built
    /// on the low-level <see cref="System.Net.WebSockets.WebSocket"/> primitives
    /// (via <c>app.UseWebSockets()</c> / <c>HttpContext.WebSockets.AcceptWebSocketAsync()</c>)
    /// rather than SignalR, to show the mechanics a higher-level framework
    /// normally hides: accepting the upgrade, tracking the connection, framing
    /// outgoing JSON messages by hand, and running a receive loop to detect
    /// when the client goes away.
    /// </summary>
    public static void MapPollWebSocket(this WebApplication app)
    {
        app.Map("/ws/polls/{pollId:guid}", async (HttpContext context, Guid pollId, PollReadService reads, PollConnectionManager connections) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync("This endpoint only accepts WebSocket upgrade requests.");
                return;
            }

            var initialPoll = await reads.GetPollAsync(pollId);
            if (initialPoll is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.WriteAsync("Poll not found.");
                return;
            }

            var pollKey = pollId.ToString();
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            connections.AddConnection(pollKey, socket);

            try
            {
                // Send a snapshot immediately so a freshly opened tab shows the
                // current tallies without waiting for someone else to vote.
                var snapshot = new PollSocketMessage("snapshot", initialPoll);
                await SendAsync(socket, JsonSerializer.Serialize(snapshot, PollJson.Options), context.RequestAborted);

                await RunReceiveLoopAsync(socket, context.RequestAborted);
            }
            finally
            {
                connections.RemoveConnection(pollKey, socket);

                if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Server closing connection", CancellationToken.None);
                    }
                    catch
                    {
                        // Best-effort close; the socket is being discarded either way.
                    }
                }
            }
        });
    }

    /// <summary>
    /// This channel is broadcast-only from the server's point of view (clients
    /// don't need to send poll data), but a WebSocket connection still requires
    /// someone to keep calling ReceiveAsync: that's how the server notices a
    /// client-initiated close handshake or an abrupt disconnect. We just discard
    /// any inbound text/binary frames and exit the loop on Close.
    /// </summary>
    private static async Task RunReceiveLoopAsync(System.Net.WebSockets.WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4 * 1024];

        while (socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            }
            catch (WebSocketException)
            {
                // Client disconnected without a clean close handshake.
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }
        }
    }

    private static Task SendAsync(System.Net.WebSockets.WebSocket socket, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }
}
