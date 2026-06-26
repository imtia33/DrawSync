using DrawSync.Models;

namespace DrawSync.Repositories.Interface
{
    public interface IOrganizationRepository : IBaseRepository<Organization>
    {
        Task<Organization?> GetByNameAsync(string name);
        Task<IEnumerable<Organization>> GetByIdsAsync(IEnumerable<string> ids);
    }
}
