using DrawSync.Models;
using DrawSync.Repositories.Interface;
using DrawSync.Data;

namespace DrawSync.Repositories.Application
{
    public class RoleRepository : BaseRepository<Role>, IRoleRepository
    {
        public RoleRepository(ApplicationDbContext context) : base(context)
        {
        }
    }
}
