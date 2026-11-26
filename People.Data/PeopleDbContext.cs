using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using People.Core.Models;
using System.Text.Json;

namespace People.Data;

/// <summary>
/// Ensures every physical SQLite connection applies WAL concurrency and a bounded busy_timeout.
/// </summary>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    public static readonly SqlitePragmaInterceptor Instance = new();

    private const string PragmaSql =
        "PRAGMA journal_mode=WAL;" +
        "PRAGMA synchronous=NORMAL;" +
        "PRAGMA busy_timeout=5000;" +
        "PRAGMA temp_store=MEMORY;" +
        "PRAGMA foreign_keys=ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = PragmaSql;
        cmd.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = PragmaSql;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}

public class PeopleDbContext : DbContext
{
    private static string SerializeJson<T>(T value) => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null);

    public DbSet<Chat> Chats { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<Contact> Contacts { get; set; } = null!;
    public DbSet<Platform> Platforms { get; set; } = null!;

    public PeopleDbContext(DbContextOptions<PeopleDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var platformColorsComparer = new ValueComparer<PlatformColors>(
            (a, b) => SerializeJson(a!) == SerializeJson(b!),
            v => SerializeJson(v).GetHashCode(),
            v => JsonSerializer.Deserialize<PlatformColors>(SerializeJson(v), (JsonSerializerOptions?)null) ?? new PlatformColors());

        var contentComparer = new ValueComparer<MessageContent>(
            (a, b) => ReferenceEquals(a, b) || (a != null && b != null && SerializeJson(a) == SerializeJson(b)),
            v => SerializeJson(v).GetHashCode(),
            v => JsonSerializer.Deserialize<MessageContent>(SerializeJson(v), (JsonSerializerOptions?)null) ?? new MessageContent());

        var reactionsComparer = new ValueComparer<List<MessageReaction>>(
            (a, b) => ReferenceEquals(a, b) || (a != null && b != null && a.Count == b.Count && SerializeJson(a) == SerializeJson(b)),
            v => SerializeJson(v).GetHashCode(),
            v => DeserializeReactions(SerializeJson(v)));

        modelBuilder.Entity<Platform>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Colors)
                  .HasConversion(
                      v => SerializeJson(v),
                      v => JsonSerializer.Deserialize<PlatformColors>(v, (JsonSerializerOptions?)null) ?? new PlatformColors(),
                      platformColorsComparer);
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.PlatformId, e.LastMessageTime });
            entity.HasMany(e => e.Participants)
                  .WithMany();
            entity.HasOne(e => e.LastMessage)
                  .WithMany()
                  .HasForeignKey("LastMessageId")
                  .IsRequired(false);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ChatId, e.Timestamp });
            entity.HasIndex(e => new { e.PlatformId, e.IsOutgoing, e.Status });
            entity.HasOne(e => e.Chat)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.ChatId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Content)
                  .HasConversion(
                      v => SerializeJson(v),
                      v => JsonSerializer.Deserialize<MessageContent>(v, (JsonSerializerOptions?)null) ?? new MessageContent(),
                      contentComparer);

            entity.Ignore(e => e.ShowDateSeparator);
            entity.Ignore(e => e.DateSeparatorText);

            entity.Property(e => e.Reactions)
                  .HasConversion(
                      v => SerializeJson(v),
                      v => DeserializeReactions(v),
                      reactionsComparer);
        });
    }

    private static List<MessageReaction> DeserializeReactions(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new List<MessageReaction>();

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return new List<MessageReaction>();

            var list = new List<MessageReaction>();
            foreach (var element in doc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String)
                {
                    var emojiStr = element.GetString();
                    if (!string.IsNullOrEmpty(emojiStr))
                    {
                        list.Add(new MessageReaction { Emoji = emojiStr, SenderId = "unknown", SenderName = "Unknown", IsFromMe = false });
                    }
                }
                else if (element.ValueKind == JsonValueKind.Object)
                {
                    var obj = JsonSerializer.Deserialize<MessageReaction>(element.GetRawText());
                    if (obj != null && !string.IsNullOrEmpty(obj.Emoji))
                    {
                        list.Add(obj);
                    }
                }
            }
            return list;
        }
        catch
        {
            return new List<MessageReaction>();
        }
    }
}
