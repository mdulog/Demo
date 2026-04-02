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
            entity.HasIndex(e => new { e.UserId, e.EventDate });

            entity.HasMany(e => e.Splits)
                .WithOne(s => s.Event)
                .HasForeignKey(s => s.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<EventSplit>(entity =>
        {
            entity.HasKey(s => s.Id);

            entity.Property(s => s.Metadata)
                .HasColumnType("jsonb")
                .HasConversion(JsonDictConverter);
        });
    }
}
