using Microsoft.EntityFrameworkCore;
using People.Core.Interfaces.Repositories;
using People.Core.Models;

namespace People.Data.Repositories;

public class ChatRepository : IChatRepository
{
    private readonly IDbContextFactory<PeopleDbContext> _contextFactory;

    public ChatRepository(IDbContextFactory<PeopleDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Chat?> GetByIdAsync(string id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var cleanId = id.StartsWith("wa_") ? id[3..] : id;
        var waId = "wa_" + cleanId;
        return await context.Chats
            .Include(c => c.Participants)
            .Include(c => c.LastMessage)
            .FirstOrDefaultAsync(c => c.Id == id || c.Id == cleanId || c.Id == waId);
    }

    public async Task<IReadOnlyList<Chat>> GetAllAsync(int offset = 0, int limit = 50)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Chats
            .Include(c => c.Participants)
            .Include(c => c.LastMessage)
            .OrderByDescending(c => c.LastMessageTime)
            .Skip(offset)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task<IReadOnlyList<Chat>> GetByPlatformAsync(string platformId, int offset = 0, int limit = 50)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Chats
            .Include(c => c.Participants)
            .Include(c => c.LastMessage)
            .Where(c => c.PlatformId == platformId)
            .OrderByDescending(c => c.LastMessageTime)
            .Skip(offset)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task SaveAsync(Chat chat)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var cleanId = chat.Id.StartsWith("wa_") ? chat.Id[3..] : chat.Id;
        var waId = "wa_" + cleanId;
        var existing = await context.Chats.FirstOrDefaultAsync(c => c.Id == chat.Id || c.Id == cleanId || c.Id == waId);
        
        if (existing == null)
        {
            var lm = chat.LastMessage;
            if (lm != null)
            {
                chat.LastMessage = null;
            }
            context.Chats.Add(chat);
            await context.SaveChangesAsync();

            if (lm != null)
            {
                chat.LastMessage = lm;
                await context.SaveChangesAsync();
            }
        }
        else
        {
            var preservedPic = existing.PictureUrl;
            var realId = existing.Id;
            context.Entry(existing).CurrentValues.SetValues(chat);
            existing.Id = realId;
            if (string.IsNullOrEmpty(chat.PictureUrl) && !string.IsNullOrEmpty(preservedPic))
            {
                existing.PictureUrl = preservedPic;
            }
            if (chat.LastMessage != null)
            {
                existing.LastMessage = chat.LastMessage;
            }
            await context.SaveChangesAsync();
        }
    }

    public async Task SaveManyAsync(IEnumerable<Chat> chats)
    {
        var chatList = chats.ToList();
        if (chatList.Count == 0) return;

        using var context = await _contextFactory.CreateDbContextAsync();
        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in chatList)
        {
            allIds.Add(c.Id);
            var clean = c.Id.StartsWith("wa_") ? c.Id[3..] : c.Id;
            allIds.Add(clean);
            allIds.Add("wa_" + clean);
        }

        var existingList = await context.Chats
            .Include(c => c.LastMessage)
            .Where(c => allIds.Contains(c.Id))
            .ToListAsync();

        var existingDict = new Dictionary<string, Chat>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in existingList)
        {
            existingDict[c.Id] = c;
            var clean = c.Id.StartsWith("wa_") ? c.Id[3..] : c.Id;
            existingDict[clean] = c;
            existingDict["wa_" + clean] = c;
        }

        var newChatsWithLastMessage = new List<(Chat Chat, Message LastMessage)>();

        foreach (var chat in chatList)
        {
            if (existingDict.TryGetValue(chat.Id, out var existing))
            {
                var preservedPic = existing.PictureUrl;
                var realId = existing.Id;
                context.Entry(existing).CurrentValues.SetValues(chat);
                existing.Id = realId;
                if (string.IsNullOrEmpty(chat.PictureUrl) && !string.IsNullOrEmpty(preservedPic))
                {
                    existing.PictureUrl = preservedPic;
                }
                if (chat.LastMessage != null)
                {
                    existing.LastMessage = chat.LastMessage;
                }
            }
            else
            {
                var lm = chat.LastMessage;
                if (lm != null)
                {
                    chat.LastMessage = null;
                    newChatsWithLastMessage.Add((chat, lm));
                }
                context.Chats.Add(chat);
                existingDict[chat.Id] = chat;
                var clean = chat.Id.StartsWith("wa_") ? chat.Id[3..] : chat.Id;
                existingDict[clean] = chat;
                existingDict["wa_" + clean] = chat;
            }
        }

        await context.SaveChangesAsync();

        if (newChatsWithLastMessage.Count > 0)
        {
            foreach (var (chat, lm) in newChatsWithLastMessage)
            {
                chat.LastMessage = lm;
            }
            await context.SaveChangesAsync();
        }
    }

    public async Task DeleteAsync(string id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var chat = await context.Chats.FindAsync(id);
        if (chat != null)
        {
            context.Chats.Remove(chat);
            await context.SaveChangesAsync();
        }
    }
}
