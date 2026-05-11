using DrawSync.Repositories.Interface;
using System;
using System.Threading.Tasks;

namespace DrawSync.UnitOfWork.Interface
{
    public interface IUnitOfWork : IDisposable
    {
        IUserRepository Users { get; }

        Task SaveChangesAsync();
    }
}
