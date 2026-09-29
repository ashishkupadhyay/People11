using Microsoft.EntityFrameworkCore;
using People.Core.Interfaces.Repositories;
using People.Core.Models;

namespace People.Data.Repositories;

public class ContactRepository : IContactRepository
{
    private readonly IDbContextFactory<PeopleDbContext> _contextFactory;

    public ContactRepository(IDbContextFactory<PeopleDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    public async Task<Contact?> GetByIdAsync(string id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Contacts.FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<IReadOnlyList<Contact>> GetAllAsync()
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        return await context.Contacts.AsNoTracking().ToListAsync();
    }

    public async Task SaveAsync(Contact contact)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var existing = await context.Contacts.FindAsync(contact.Id);
        
        if (existing == null)
        {
            context.Contacts.Add(contact);
        }
        else
        {
            context.Entry(existing).CurrentValues.SetValues(contact);
        }
        
        await context.SaveChangesAsync();
    }

    public async Task DeleteAsync(string id)
    {
        using var context = await _contextFactory.CreateDbContextAsync();
        var contact = await context.Contacts.FindAsync(id);
        if (contact != null)
        {
            context.Contacts.Remove(contact);
            await context.SaveChangesAsync();
        }
    }
}
