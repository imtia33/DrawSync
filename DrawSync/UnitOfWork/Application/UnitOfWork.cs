using DrawSync.Repositories.Interface;
using DrawSync.UnitOfWork.Interface;
using System.Threading.Tasks;

namespace DrawSync.UnitOfWork.Application
{
    public class UnitOfWork : IUnitOfWork
    {
        public UnitOfWork(IUserRepository userRepository)
        {
            Users = userRepository;
        }

        public IUserRepository Users { get; }

        public async Task SaveChangesAsync()
        {
            // Appwrite operations are immediate in our BaseRepository, 
            // but we keep this for consistency in the UoW pattern.
            await Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}
