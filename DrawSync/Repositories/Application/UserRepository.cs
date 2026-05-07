using DrawSync.Models;
using DrawSync.Repositories.Interface;
using DrawSync.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

namespace DrawSync.Repositories.Application
{
    public class UserRepository : BaseRepository<User>, IUserRepository
    {
        public UserRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            return await _dbSet.Include(u => u.Role)
                               .FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User?> GetProfileAsync(int id)
        {
            return await _dbSet.Include(u => u.Role)
                               .FirstOrDefaultAsync(u => u.Id == id);
        }
    }
}
