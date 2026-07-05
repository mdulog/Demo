using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pacevite.Api.Domain.Entities;
using Pacevite.Api.Domain.Enums;

namespace Pacevite.Api.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventSplit> EventSplits => Set<EventSplit>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<SyncConnection> SyncConnections => Set<SyncConnection>();

    private static readonly ValueConverter<Dictionary<string, object>, string> JsonDictConverter = new(
        v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
        v => JsonSerializer.Deserialize<Dictionary<string, object>>(v, (JsonSerializerOptions?)null) ?? new()
    );

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Event>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.EventType)
                .HasConversion<string>();

            entity.Property(e => e.Completion)
                .HasConversion<string>();

            // JSONB columns — enables GIN indexing and structured querying without
            // separate tables per event type (see normalization model)
            entity.Property(e => e.Location)
                .HasColumnType("jsonb")
                .HasConversion(JsonDictConverter);

            entity.Property(e => e.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(JsonDictConverter);

            // GIN index on metadata for future station/obstacle queries
            entity.HasIndex(e => e.Metadata)
                .HasMethod("GIN");

            entity.HasIndex(e => new { e.UserId, e.EventType });
            // Id tiebreaker makes the keyset ORDER BY (EventDate DESC, Id DESC) fully index-served.
            // Postgres btree scans backwards, so ASC columns serve the DESC walk.
            entity.HasIndex(e => new { e.UserId, e.EventDate, e.Id });

            entity.HasMany(e => e.Splits)
                .WithOne(s => s.Event)
                .HasForeignKey(s => s.EventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.SyncConnection)
                .WithMany(sc => sc.Events)
                .HasForeignKey(e => e.SyncConnectionId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        builder.Entity<EventSplit>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(JsonDictConverter);
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(rt => rt.Id);
            entity.HasIndex(rt => rt.TokenHash).IsUnique();
            entity.HasIndex(rt => new { rt.UserId, rt.RevokedAt });
            entity.Ignore(rt => rt.IsExpired);
            entity.Ignore(rt => rt.IsActive);
        });

        builder.Entity<SyncConnection>(entity =>
        {
            entity.HasKey(sc => sc.Id);

            entity.Property(sc => sc.Platform)
                .HasConversion<string>();

            entity.HasIndex(sc => new { sc.UserId, sc.Platform }).IsUnique();
        });
    }
}
