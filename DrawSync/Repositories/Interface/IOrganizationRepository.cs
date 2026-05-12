using DrawSync.Models;

namespace DrawSync.Repositories.Interface
{
    public interface IOrganizationRepository : IBaseRepository<Organization>
    {
        Task<Organization?> GetByNameAsync(string name);
    }
}
