using Assistant.Net.Data.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Assistant.Net.Data.Repositories.Impl;

public class UnitOfWorkFactory(IDbContextFactory<AssistantDbContext> dbFactory) : IUnitOfWorkFactory
{
    public async Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default)
    {
        var context = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        return new UnitOfWork(context);
    }
}