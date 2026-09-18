using Microsoft.EntityFrameworkCore;
using PollingApp.WebSockets.Data;
using PollingApp.WebSockets.Endpoints;
using PollingApp.WebSockets.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Data Source=polling.db";

builder.Services.AddDbContext<PollDbContext>(options => options.UseSqlite(connectionString));

builder.Services.AddScoped<PollReadService>();

// One connection manager for the whole app's lifetime: it is the in-memory
// source of truth for "which WebSocket is watching which poll", so it must be
// a singleton, not a per-request scoped service.
builder.Services.AddSingleton<PollConnectionManager>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

// Create the SQLite database/schema on first run. A portfolio-scale demo app
// doesn't need a migrations pipeline; EnsureCreated() is enough to get a
// working schema from the model above.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PollDbContext>();
    db.Database.EnsureCreated();
}

app.UseCors();

// Must come before UseStaticFiles/endpoint mapping so the WebSocket upgrade
// handshake is recognized for requests to /ws/polls/{pollId}.
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapPollEndpoints();
app.MapPollWebSocket();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
