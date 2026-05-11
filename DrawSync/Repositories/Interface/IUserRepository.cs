using DrawSync.Models;
using DrawSync.Repositories.Interface;

namespace DrawSync.Repositories.Interface
{
    public interface IUserRepository : IBaseRepository<User>
    {
        Task<User?> GetByEmailAsync(string email);
        Task<User?> GetProfileAsync(string id);
    }
}
