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

    public async Task<IReadOnlyList<Message>> GetByChatIdAsync(string chatId, int offset = 0, int limit = 50)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var cleanId = chatId.StartsWith("wa_") ? chatId.Substring(3) : chatId;
        var waId = "wa_" + cleanId;
        var msgs = await context.Messages
            .Where(m => m.ChatId == cleanId || m.ChatId == waId)
            .OrderByDescending(m => m.Timestamp)
            .Skip(offset)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
        msgs.Reverse();
        return msgs;
    }

    public async Task<IReadOnlyList<Message>> GetOlderMessagesBeforeAsync(string chatId, DateTimeOffset beforeTimestamp, int limit = 25)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var cleanId = chatId.StartsWith("wa_") ? chatId.Substring(3) : chatId;
        var waId = "wa_" + cleanId;
        var msgs = await context.Messages
            .Where(m => (m.ChatId == cleanId || m.ChatId == waId) && m.Timestamp < beforeTimestamp)
            .OrderByDescending(m => m.Timestamp)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
        msgs.Reverse();
        return msgs;
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
            context.Entry(existing).CurrentValues.SetValues(message);
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
                context.Entry(existing).CurrentValues.SetValues(message);
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
