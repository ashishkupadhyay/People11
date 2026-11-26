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

    public async Task<IReadOnlyList<Message>> GetOlderMessagesBeforeAsync(
        string chatId,
        DateTimeOffset beforeTimestamp,
        int limit = 25,
        string? excludeMessageId = null)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var candidates = GetCandidateChatIds(chatId);
        // Include boundary timestamp when excludeMessageId is provided so same-second messages are never skipped.
        int fetchLimit = string.IsNullOrEmpty(excludeMessageId) ? limit : limit + 15;
        IQueryable<Message> query = string.IsNullOrEmpty(excludeMessageId)
            ? context.Messages.Where(m => candidates.Contains(m.ChatId!) && m.Timestamp < beforeTimestamp)
            : context.Messages.Where(m => candidates.Contains(m.ChatId!) && m.Timestamp <= beforeTimestamp && m.Id != excludeMessageId);

        var msgs = await query
            .OrderByDescending(m => m.Timestamp)
            .ThenByDescending(m => m.Id)
            .Take(fetchLimit)
            .AsNoTracking()
            .ToListAsync();

        if (!string.IsNullOrEmpty(excludeMessageId))
        {
            msgs = msgs
                .Where(m => m.Timestamp < beforeTimestamp ||
                            (m.Timestamp == beforeTimestamp && string.CompareOrdinal(m.Id, excludeMessageId) < 0))
                .Take(limit)
                .ToList();
        }

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

            var conn = context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
            {
                await conn.OpenAsync();
            }

            int userVersion = 0;
            await using (var verCmd = conn.CreateCommand())
            {
                verCmd.CommandText = "PRAGMA user_version;";
                var scalar = await verCmd.ExecuteScalarAsync();
                if (scalar != null && scalar != DBNull.Value)
                {
                    userVersion = Convert.ToInt32(scalar);
                }
            }

            if (userVersion >= 2)
            {
                return;
            }

            var existingCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var colCmd = conn.CreateCommand())
            {
                colCmd.CommandText = "PRAGMA table_info(Messages);";
                await using var reader = await colCmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    existingCols.Add(reader.GetString(1));
                }
            }

            if (!existingCols.Contains("IsEdited"))
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Messages ADD COLUMN IsEdited INTEGER NOT NULL DEFAULT 0;");
            }
            if (!existingCols.Contains("QuotedMessageId"))
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Messages ADD COLUMN QuotedMessageId TEXT NULL;");
            }
            if (!existingCols.Contains("QuotedParticipant"))
            {
                await context.Database.ExecuteSqlRawAsync("ALTER TABLE Messages ADD COLUMN QuotedParticipant TEXT NULL;");
            }

            await context.Database.ExecuteSqlRawAsync("CREATE INDEX IF NOT EXISTS \"IX_Messages_PlatformId_IsOutgoing_Status\" ON \"Messages\" (\"PlatformId\", \"IsOutgoing\", \"Status\");");

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
                        context.Entry(m).Property(e => e.Content).IsModified = true;
                    }
                }
            }

            if (anyChanged)
            {
                await context.SaveChangesAsync();
            }

            await context.Database.ExecuteSqlRawAsync("PRAGMA user_version = 2;");
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
        await EnsureValidChatIdsAsync(context, new[] { message });
        var existing = await context.Messages.FindAsync(message.Id);

        if (existing == null)
        {
            context.Messages.Add(message);
        }
        else
        {
            ApplyMessageUpdates(existing, message);
        }

        await context.SaveChangesAsync();
    }

    public async Task SaveManyAsync(IEnumerable<Message> messages)
    {
        var msgList = messages
            .Where(m => !string.IsNullOrWhiteSpace(m.Id))
            .GroupBy(m => m.Id, StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();
        if (msgList.Count == 0) return;

        using var context = await _contextFactory.CreateDbContextAsync();

        // Batch-resolve Chat foreign keys and create any missing stubs in one round-trip
        await EnsureValidChatIdsAsync(context, msgList);

        // Batch-load existing messages in chunks of 400 to avoid SQLite parameter limit
        var existingById = new Dictionary<string, Message>(StringComparer.Ordinal);
        for (int i = 0; i < msgList.Count; i += 400)
        {
            var chunkIds = msgList.Skip(i).Take(400).Select(m => m.Id).ToList();
            var existingChunk = await context.Messages
                .Where(m => chunkIds.Contains(m.Id))
                .ToListAsync();
            foreach (var ex in existingChunk)
            {
                existingById[ex.Id] = ex;
            }
        }

        foreach (var message in msgList)
        {
            if (existingById.TryGetValue(message.Id, out var existing))
            {
                ApplyMessageUpdates(existing, message);
            }
            else
            {
                context.Messages.Add(message);
                existingById[message.Id] = message;
            }
        }

        await context.SaveChangesAsync();
    }

    private static void ApplyMessageUpdates(Message existing, Message incoming)
    {
        if (incoming.IsOutgoing && existing.Status > incoming.Status)
        {
            incoming.Status = existing.Status;
        }
        if (existing.Status != incoming.Status) existing.Status = incoming.Status;
        if (existing.SenderName != incoming.SenderName && !string.IsNullOrEmpty(incoming.SenderName))
        {
            existing.SenderName = incoming.SenderName;
        }
        if (existing.IsEdited != incoming.IsEdited) existing.IsEdited = incoming.IsEdited;
        if (incoming.Content != null) existing.Content = incoming.Content;
        if (incoming.Timestamp != default && existing.Timestamp != incoming.Timestamp) existing.Timestamp = incoming.Timestamp;
        if (!string.IsNullOrEmpty(incoming.QuotedText)) existing.QuotedText = incoming.QuotedText;
        if (!string.IsNullOrEmpty(incoming.QuotedSender)) existing.QuotedSender = incoming.QuotedSender;
        if (!string.IsNullOrEmpty(incoming.QuotedMessageId)) existing.QuotedMessageId = incoming.QuotedMessageId;
        if (!string.IsNullOrEmpty(incoming.QuotedParticipant)) existing.QuotedParticipant = incoming.QuotedParticipant;
        if (incoming.Reactions != null && incoming.Reactions.Count > 0) existing.Reactions = incoming.Reactions;
    }

    private static async Task EnsureValidChatIdsAsync(PeopleDbContext context, IEnumerable<Message> messages)
    {
        var candidateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var byOriginalChat = new List<Message>();
        foreach (var m in messages)
        {
            m.Chat = null;
            if (!string.IsNullOrWhiteSpace(m.ChatId))
            {
                byOriginalChat.Add(m);
                var cleanId = m.ChatId.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? m.ChatId[3..] : m.ChatId;
                candidateKeys.Add(m.ChatId);
                candidateKeys.Add(cleanId);
                candidateKeys.Add("wa_" + cleanId);
            }
        }
        if (candidateKeys.Count == 0) return;

        var keyList = candidateKeys.ToList();
        var chatLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < keyList.Count; i += 400)
        {
            var chunk = keyList.Skip(i).Take(400).ToList();
            var foundIds = await context.Chats
                .AsNoTracking()
                .Where(c => chunk.Contains(c.Id))
                .Select(c => c.Id)
                .ToListAsync();
            foreach (var id in foundIds)
            {
                chatLookup[id] = id;
                var clean = id.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? id[3..] : id;
                chatLookup.TryAdd(clean, id);
                chatLookup.TryAdd("wa_" + clean, id);
            }
        }

        var missingChats = new List<Chat>();
        foreach (var m in byOriginalChat)
        {
            if (chatLookup.TryGetValue(m.ChatId!, out var resolvedChatId))
            {
                m.ChatId = resolvedChatId;
            }
            else
            {
                var stub = new Chat
                {
                    Id = m.ChatId!,
                    PlatformId = m.PlatformId ?? "WhatsApp",
                    Title = m.SenderName ?? "Chat",
                    LastMessageTime = m.Timestamp
                };
                missingChats.Add(stub);
                var clean = stub.Id.StartsWith("wa_", StringComparison.OrdinalIgnoreCase) ? stub.Id[3..] : stub.Id;
                chatLookup[stub.Id] = stub.Id;
                chatLookup.TryAdd(clean, stub.Id);
                chatLookup.TryAdd("wa_" + clean, stub.Id);
            }
        }

        if (missingChats.Count > 0)
        {
            context.Chats.AddRange(missingChats);
            await context.SaveChangesAsync();
            foreach (var stub in missingChats)
            {
                context.Entry(stub).State = EntityState.Detached;
            }
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
