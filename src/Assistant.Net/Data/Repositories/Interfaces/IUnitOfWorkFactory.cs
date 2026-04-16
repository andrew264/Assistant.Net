namespace Assistant.Net.Data.Repositories.Interfaces;

public interface IUnitOfWorkFactory
{
    Task<IUnitOfWork> CreateAsync(CancellationToken cancellationToken = default);
}