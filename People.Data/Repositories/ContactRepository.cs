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

    public async Task SaveManyAsync(IEnumerable<Contact> contacts)
    {
        var list = contacts
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (list.Count == 0) return;

        using var context = await _contextFactory.CreateDbContextAsync();
        var existingMap = new Dictionary<string, Contact>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < list.Count; i += 400)
        {
            var chunkIds = list.Skip(i).Take(400).Select(c => c.Id).ToList();
            var existingChunk = await context.Contacts
                .Where(c => chunkIds.Contains(c.Id))
                .ToListAsync();
            foreach (var c in existingChunk)
            {
                existingMap[c.Id] = c;
            }
        }

        foreach (var contact in list)
        {
            if (existingMap.TryGetValue(contact.Id, out var existing))
            {
                if (!string.Equals(existing.Name, contact.Name, StringComparison.Ordinal) ||
                    !string.Equals(existing.PhoneNumber, contact.PhoneNumber, StringComparison.Ordinal))
                {
                    context.Entry(existing).CurrentValues.SetValues(contact);
                }
            }
            else
            {
                context.Contacts.Add(contact);
                existingMap[contact.Id] = contact;
            }
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
