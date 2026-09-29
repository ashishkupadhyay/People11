using Microsoft.EntityFrameworkCore;
using People.Core.Models;
using System.Text.Json;

namespace People.Data;

public class PeopleDbContext : DbContext
{
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

        modelBuilder.Entity<Platform>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Colors)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                      v => JsonSerializer.Deserialize<PlatformColors>(v, (JsonSerializerOptions?)null) ?? new PlatformColors());
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(e => e.Id);
        });

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id);
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
            entity.HasOne(e => e.Chat)
                  .WithMany(c => c.Messages)
                  .HasForeignKey(e => e.ChatId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.Content)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                      v => JsonSerializer.Deserialize<MessageContent>(v, (JsonSerializerOptions?)null) ?? new MessageContent());
        });
    }
}
