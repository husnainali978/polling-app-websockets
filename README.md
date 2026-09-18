# Live Polls — Minimal API + Raw WebSockets

A live polling/survey app where votes show up in every connected browser tab
the instant they're cast — no page refresh, no client-side polling.

## Why this exists

Most real-time .NET demos reach straight for SignalR, and SignalR is the
right call for production apps. But it also hides almost everything
interesting: connection tracking, groups, message framing, and reconnection
are all handled for you. This project intentionally skips that abstraction
and talks to `System.Net.WebSockets` directly — `app.UseWebSockets()`,
`HttpContext.WebSockets.AcceptWebSocketAsync()`, manual send/receive loops —
to show the mechanics a framework like SignalR is built on top of. It's the
lower-level counterpart to a SignalR-based project elsewhere in this
portfolio.

## Architecture

**Stack:** ASP.NET Core Minimal API (net10.0) for HTTP, raw
`System.Net.WebSockets` for real-time push, EF Core + SQLite for persistence,
and a dependency-free HTML/CSS/JS page for the demo client.

```
Browser tab A                         ASP.NET Core process
┌────────────────────┐                ┌───────────────────────────────────┐
│ fetch POST          │──vote────────▶│ POST /api/polls/{id}/vote          │
│ /api/polls/.../vote │                │   1. write Vote row (EF Core)      │
└────────────────────┘                │   2. re-read tallies               │
                                       │   3. connections.BroadcastAsync()  │
Browser tab A          ◀───update─────│        │                           │
Browser tab B          ◀───update─────│        ▼                           │
Browser tab C          ◀───update─────│  PollConnectionManager             │
  (native WebSocket)                  │  ConcurrentDictionary<pollId,      │
                                       │    List<WebSocket>>                │
                                       └───────────────────────────────────┘
```

**The connection manager** (`Services/PollConnectionManager.cs`) is the piece
doing the work a library would otherwise hide. It's a
`ConcurrentDictionary<string, List<WebSocket>>` keyed by poll id:

- `AddConnection` / `RemoveConnection` run whenever a socket opens or closes,
  registering it against the poll it's watching.
- The dictionary itself is safe for concurrent access out of the box; each
  poll's `List<WebSocket>` is not, so every read or mutation of a given list
  takes a `lock` on that list instance. Different polls never contend on the
  same lock, since each gets its own list.
- `BroadcastAsync` snapshots the live sockets for a poll under the lock, then
  sends outside the lock (`SendAsync` is network I/O and shouldn't block other
  threads touching that poll's connection list). Failed sends are swallowed —
  a socket that's gone stale gets cleaned up by its own receive loop, not by
  the broadcaster.

**The broadcast flow**: a vote is a normal POST handled by a Minimal API
endpoint. It writes the vote with EF Core, re-queries the poll's current
tallies, serializes them to JSON, and calls
`PollConnectionManager.BroadcastAsync(pollId, json)`. That's it — the HTTP
request that cast the vote and the WebSocket clients watching that poll are
otherwise unrelated; the connection manager is the only thing that ties them
together.

**The WebSocket endpoint** (`Endpoints/WebSocketEndpoints.cs`,
`/ws/polls/{pollId}`) accepts the upgrade, registers the socket, immediately
pushes a `snapshot` message so a freshly opened tab isn't blank, then runs a
receive loop whose only job is noticing when the client disconnects (this
channel is broadcast-only — the server never needs data back from the
client). On close or error the socket is unregistered in a `finally` block.

## Features

- Create a poll with a question and 2+ options
- List all polls with live vote totals
- Fetch a single poll's current tallies
- Vote on an option (`POST /api/polls/{id}/vote`)
- Real-time tally broadcast to every browser tab watching a poll, over a raw
  WebSocket, the moment a vote is cast
- Thread-safe in-memory connection registry, scoped per poll
- EF Core + SQLite persistence across polls, options, and individual votes
- Zero-dependency static demo page (`wwwroot/`) using the native browser
  `WebSocket` API — no client libraries, no SignalR client

## How to run it

```bash
# from the repo root
dotnet restore src/PollingApp.WebSockets/PollingApp.WebSockets.csproj -s https://api.nuget.org/v3/index.json
dotnet run --project src/PollingApp.WebSockets/PollingApp.WebSockets.csproj
```

The app listens on `http://localhost:5080` (see
`src/PollingApp.WebSockets/Properties/launchSettings.json`) and serves the
demo page at `/`. A SQLite database file (`polling.db`) is created
automatically on first run via `EnsureCreated()` — no separate migration step
needed for this demo scale.

**To see the live update in action:**

1. Open `http://localhost:5080` in a browser tab and create a poll (a
   question plus at least two options).
2. Copy the page URL — after selecting a poll it becomes
   `http://localhost:5080/?poll=<poll-id>`.
3. Open that same URL in a second tab (or a second browser window).
4. Vote in one tab. The tally bars and totals in the *other* tab update
   immediately, pushed over its own open WebSocket connection — no refresh,
   no polling interval.
5. Expand "WebSocket message log" on either tab to see the raw `snapshot` /
   `update` JSON frames as they arrive.

## Tech stack

ASP.NET Core Minimal APIs (net10.0), raw `System.Net.WebSockets`, EF Core with SQLite, vanilla HTML/CSS/JS.
