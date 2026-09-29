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
        return await context.Messages
            .Where(m => m.ChatId == chatId)
            .OrderByDescending(m => m.Timestamp)
            .Skip(offset)
            .Take(limit)
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task SaveAsync(Message message)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
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
