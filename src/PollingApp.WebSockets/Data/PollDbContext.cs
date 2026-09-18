using Microsoft.EntityFrameworkCore;
using PollingApp.WebSockets.Models;

namespace PollingApp.WebSockets.Data;

public class PollDbContext : DbContext
{
    public PollDbContext(DbContextOptions<PollDbContext> options) : base(options)
    {
    }

    public DbSet<Poll> Polls => Set<Poll>();

    public DbSet<PollOption> PollOptions => Set<PollOption>();

    public DbSet<Vote> Votes => Set<Vote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Poll>(poll =>
        {
            poll.HasKey(p => p.Id);
            poll.Property(p => p.Question).IsRequired().HasMaxLength(500);
            poll.HasMany(p => p.Options)
                .WithOne(o => o.Poll)
                .HasForeignKey(o => o.PollId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PollOption>(option =>
        {
            option.HasKey(o => o.Id);
            option.Property(o => o.Text).IsRequired().HasMaxLength(200);
            option.HasMany(o => o.Votes)
                .WithOne(v => v.PollOption)
                .HasForeignKey(v => v.PollOptionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Vote>(vote =>
        {
            vote.HasKey(v => v.Id);
        });
    }
}
