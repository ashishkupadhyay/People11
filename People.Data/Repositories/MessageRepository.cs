using Microsoft.EntityFrameworkCore;
using People.Core.Interfaces.Repositories;
using People.Core.Models;

namespace People.Data.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly IDbContextFactory<PeopleDbContext> _contextFactory;

    public MessageRepository(IDbContextFactory<PeopleDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Message?> GetByIdAsync(string id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Messages.FirstOrDefaultAsync(m => m.Id == id);
    }

    private static List<string> GetCandidateChatIds(string chatId)
    {
        var cleanId = chatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? chatId.Substring(3) : chatId;
        var list = new List<string> { cleanId, "wa_" + cleanId };
        if (cleanId.Contains('@'))
        {
            var parts = cleanId.Split('@');
            var user = parts[0];
            var domain = parts[1];
            var bareUser = user.Split(':')[0];
            list.Add($"{bareUser}@{domain}");
            list.Add($"wa_{bareUser}@{domain}");
            list.Add($"{bareUser}:0@{domain}");
            list.Add($"wa_{bareUser}:0@{domain}");
        }
        else
        {
            list.Add($"{cleanId}@s.whatsapp.net");
            list.Add($"wa_{cleanId}@s.whatsapp.net");
            list.Add($"{cleanId}@g.us");
            list.Add($"wa_{cleanId}@g.us");
        }
        return list.Distinct().ToList();
    }

    public async Task<IReadOnlyList<Message>> GetByChatIdAsync(string chatId, int offset = 0, int limit = 50)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var candidates = GetCandidateChatIds(chatId);
        var msgs = await context.Messages
            .Where(m => candidates.Contains(m.ChatId!))
            .OrderByDescending(m => m.Timestamp)
            .Skip(offset)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
        msgs.Reverse();
        foreach (var m in msgs)
        {
            HealLegacyMessage(m);
        }
        return msgs;
    }

    public async Task<IReadOnlyList<Message>> GetOlderMessagesBeforeAsync(string chatId, DateTimeOffset beforeTimestamp, int limit = 25)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var candidates = GetCandidateChatIds(chatId);
        var msgs = await context.Messages
            .Where(m => candidates.Contains(m.ChatId!) && m.Timestamp < beforeTimestamp)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
        msgs.Reverse();
        foreach (var m in msgs)
        {
            HealLegacyMessage(m);
        }
        return msgs;
    }

    private static void HealLegacyMessage(Message m)
    {
        if (m.Content == null) return;
        if (m.Content.Type == MessageContentType.Text && !string.IsNullOrWhiteSpace(m.Content.Text))
        {
            var text = m.Content.Text.Trim();
            if (text == "📷 Photo" || text.StartsWith("📷 Photo:"))
            {
                m.Content.Type = MessageContentType.Image;
                if (text.Length > 9 && text.StartsWith("📷 Photo: "))
                {
                    m.Content.Caption = text[10..].Trim();
                }
                m.Content.Text = string.Empty;
            }
            else if (text == "🎥 Video" || text.StartsWith("🎥 Video:"))
            {
                m.Content.Type = MessageContentType.Video;
                if (text.Length > 9 && text.StartsWith("🎥 Video: "))
                {
                    m.Content.Caption = text[10..].Trim();
                }
                m.Content.Text = string.Empty;
            }
            else if (text.StartsWith("📄 "))
            {
                m.Content.Type = MessageContentType.Document;
                m.Content.FileName = text.Length > 2 ? text[2..].Trim() : "Document";
                m.Content.Text = string.Empty;
            }
            else if (text == "🎤 Voice message" || text == "🎵 Audio")
            {
                m.Content.Type = MessageContentType.Audio;
                m.Content.Text = string.Empty;
            }
        }
    }

    public async Task HealPersistedLegacyMessagesAsync()
    {
        try
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var candidates = await context.Messages
                .Where(m => m.Content != null)
                .ToListAsync();

            bool anyChanged = false;
            foreach (var m in candidates)
            {
                if (m.Content != null && m.Content.Type == MessageContentType.Text && !string.IsNullOrWhiteSpace(m.Content.Text))
                {
                    var oldText = m.Content.Text;
                    HealLegacyMessage(m);
                    if (m.Content.Type != MessageContentType.Text || m.Content.Text != oldText)
                    {
                        anyChanged = true;
                    }
                }
            }

            if (anyChanged)
            {
                await context.SaveChangesAsync();
            }
        }
        catch { }
    }

    public async Task<IReadOnlyList<Message>> GetPendingOutgoingMessagesAsync(string platformId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Messages
            .Where(m => m.PlatformId == platformId && m.IsOutgoing && m.Status == MessageStatus.Sending)
            .OrderBy(m => m.Timestamp)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task SaveAsync(Message message)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        await EnsureValidChatIdAsync(context, message);
        var existing = await context.Messages.FindAsync(message.Id);
        
        if (existing == null)
        {
            context.Messages.Add(message);
        }
        else
        {
            if (message.IsOutgoing && existing.Status > message.Status)
            {
                message.Status = existing.Status;
            }
            existing.Status = message.Status;
            existing.Content = message.Content;
            existing.SenderName = message.SenderName;
            existing.Timestamp = message.Timestamp;
            existing.QuotedText = message.QuotedText;
            existing.QuotedSender = message.QuotedSender;
            existing.Reactions = message.Reactions;
            context.Entry(existing).State = EntityState.Modified;
        }
        
        await context.SaveChangesAsync();
    }

    public async Task SaveManyAsync(IEnumerable<Message> messages)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        foreach (var message in messages)
        {
            await EnsureValidChatIdAsync(context, message);
            var existing = await context.Messages.FindAsync(message.Id);
            if (existing == null)
            {
                context.Messages.Add(message);
            }
            else
            {
                if (message.IsOutgoing && existing.Status > message.Status)
                {
                    message.Status = existing.Status;
                }
                existing.Status = message.Status;
                existing.Content = message.Content;
                existing.SenderName = message.SenderName;
                existing.Timestamp = message.Timestamp;
                existing.QuotedText = message.QuotedText;
                existing.QuotedSender = message.QuotedSender;
                existing.Reactions = message.Reactions;
                context.Entry(existing).State = EntityState.Modified;
            }
        }
        await context.SaveChangesAsync();
    }

    private static async Task EnsureValidChatIdAsync(PeopleDbContext context, Message message)
    {
        message.Chat = null;
        if (string.IsNullOrEmpty(message.ChatId)) return;

        var cleanId = message.ChatId.StartsWith("wa_") ? message.ChatId[3..] : message.ChatId;
        var waId = "wa_" + cleanId;

        var matchingChat = await context.Chats.FirstOrDefaultAsync(c => c.Id == message.ChatId || c.Id == cleanId || c.Id == waId);
        if (matchingChat != null)
        {
            message.ChatId = matchingChat.Id;
        }
        else
        {
            var newChat = new Chat
            {
                Id = message.ChatId,
                PlatformId = message.PlatformId ?? "WhatsApp",
                Title = message.SenderName ?? "Chat",
                LastMessageTime = message.Timestamp
            };
            context.Chats.Add(newChat);
            try
            {
                await context.SaveChangesAsync();
            }
            catch { }
        }
    }

    public async Task DeleteAsync(string id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var message = await context.Messages.FindAsync(id);
        if (message != null)
        {
            context.Messages.Remove(message);
            await context.SaveChangesAsync();
        }
    }
}
