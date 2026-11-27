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

    public async Task<Dictionary<string, string>> GetPictureUrlsByPlatformAsync(string platformId)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Chats
            .AsNoTracking()
            .Where(c => c.PlatformId == platformId && !string.IsNullOrEmpty(c.PictureUrl))
            .Select(c => new { c.Id, PictureUrl = c.PictureUrl! })
            .ToDictionaryAsync(x => x.Id, x => x.PictureUrl);
    }

    public async Task SaveAsync(Chat chat)
    {
        await SaveManyAsync(new[] { chat });
    }

    public async Task SaveManyAsync(IEnumerable<Chat> chats)
    {
        var chatList = chats
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (chatList.Count == 0) return;

        using var context = await _contextFactory.CreateDbContextAsync();
        var allIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lmById = new Dictionary<string, Message>(StringComparer.Ordinal);
        foreach (var c in chatList)
        {
            allIds.Add(c.Id);
            var clean = c.Id.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? c.Id[3..] : c.Id;
            allIds.Add(clean);
            allIds.Add("wa_" + clean);
            if (c.LastMessage != null && !string.IsNullOrWhiteSpace(c.LastMessage.Id))
            {
                c.LastMessage.Chat = null;
                c.LastMessage.ChatId = c.Id;
                lmById.TryAdd(c.LastMessage.Id, c.LastMessage);
            }
        }

        // Batch-query existing Chats WITHOUT Include(LastMessage) to avoid duplicate tracked Message instances
        var existingList = new List<Chat>();
        var idList = allIds.ToList();
        for (int i = 0; i < idList.Count; i += 400)
        {
            var chunk = idList.Skip(i).Take(400).ToList();
            var chunkChats = await context.Chats
                .Where(c => chunk.Contains(c.Id))
                .ToListAsync();
            existingList.AddRange(chunkChats);
        }

        var existingDict = new Dictionary<string, Chat>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in existingList)
        {
            existingDict[c.Id] = c;
            var clean = c.Id.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? c.Id[3..] : c.Id;
            existingDict.TryAdd(clean, c);
            existingDict.TryAdd("wa_" + clean, c);
        }

        // Batch-load existing LastMessages so Status/Edit/Revoke updates persist cleanly
        var existingMsgsById = new Dictionary<string, Message>(StringComparer.Ordinal);
        var lmIds = lmById.Keys.ToList();
        for (int i = 0; i < lmIds.Count; i += 400)
        {
            var chunk = lmIds.Skip(i).Take(400).ToList();
            var foundMsgs = await context.Messages
                .Where(m => chunk.Contains(m.Id))
                .ToListAsync();
            foreach (var m in foundMsgs)
            {
                existingMsgsById[m.Id] = m;
            }
        }

        var pendingLastMsgLinks = new List<(Chat TargetChat, string MessageId, Message? NewMessage)>();

        foreach (var chat in chatList)
        {
            var incomingLm = chat.LastMessage;
            chat.LastMessage = null;

            if (incomingLm != null && !string.IsNullOrWhiteSpace(incomingLm.Id) &&
                existingMsgsById.TryGetValue(incomingLm.Id, out var existingDbMsg))
            {
                if (incomingLm.Status > existingDbMsg.Status) existingDbMsg.Status = incomingLm.Status;
                if (incomingLm.IsEdited) existingDbMsg.IsEdited = true;
                if (incomingLm.IsRevoked)
                {
                    existingDbMsg.IsRevoked = true;
                    if (existingDbMsg.Content != null && incomingLm.Content != null)
                    {
                        existingDbMsg.Content.IsRevoked = true;
                        existingDbMsg.Content.Type = MessageContentType.Text;
                        existingDbMsg.Content.Text = incomingLm.Content.Text;
                    }
                }
            }

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

                if (incomingLm != null && !string.IsNullOrWhiteSpace(incomingLm.Id))
                {
                    incomingLm.ChatId = realId;
                    pendingLastMsgLinks.Add((existing, incomingLm.Id, existingMsgsById.ContainsKey(incomingLm.Id) ? null : incomingLm));
                }
            }
            else
            {
                var copy = new Chat
                {
                    Id = chat.Id,
                    PlatformId = chat.PlatformId,
                    Title = chat.Title,
                    IsGroup = chat.IsGroup,
                    UnreadCount = chat.UnreadCount,
                    PictureUrl = chat.PictureUrl,
                    LastMessageTime = chat.LastMessageTime
                };
                context.Chats.Add(copy);
                existingDict[copy.Id] = copy;
                var clean = copy.Id.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? copy.Id[3..] : copy.Id;
                existingDict.TryAdd(clean, copy);
                existingDict.TryAdd("wa_" + clean, copy);

                if (incomingLm != null && !string.IsNullOrWhiteSpace(incomingLm.Id))
                {
                    incomingLm.ChatId = copy.Id;
                    pendingLastMsgLinks.Add((copy, incomingLm.Id, existingMsgsById.ContainsKey(incomingLm.Id) ? null : incomingLm));
                }
            }

            chat.LastMessage = incomingLm;
        }

        await context.SaveChangesAsync();

        if (pendingLastMsgLinks.Count > 0)
        {
            var addedMsgIds = new HashSet<string>(existingMsgsById.Keys, StringComparer.Ordinal);
            foreach (var (targetChat, msgId, newMsg) in pendingLastMsgLinks)
            {
                if (newMsg != null && addedMsgIds.Add(msgId))
                {
                    context.Messages.Add(newMsg);
                }
                context.Entry(targetChat).Property<string?>("LastMessageId").CurrentValue = msgId;
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
