using DrawSync.Data;
using DrawSync.Repositories.Interface;
using DrawSync.UnitOfWork.Interface;
using System.Threading.Tasks;

namespace DrawSync.UnitOfWork.Application
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ApplicationDbContext _context;

        public UnitOfWork(
            ApplicationDbContext context,
            IUserRepository userRepository,
            IRoleRepository roleRepository)
        {
            _context = context;
            Users = userRepository;
            Roles = roleRepository;
        }

        public IUserRepository Users { get; }
        public IRoleRepository Roles { get; }

        public async Task<int> SaveChangesAsync() =>
            await _context.SaveChangesAsync();

        public void Dispose() =>
            _context.Dispose();
    }
}
