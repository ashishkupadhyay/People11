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
        return await context.Chats
            .Include(c => c.Participants)
            .Include(c => c.LastMessage)
            .FirstOrDefaultAsync(c => c.Id == id);
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
        var existing = await context.Chats.FindAsync(chat.Id);
        
        if (existing == null)
        {
            context.Chats.Add(chat);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(chat);
            // Updating relationships requires more complex logic in a real app
        }
        
        await context.SaveChangesAsync();
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
