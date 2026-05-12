using DrawSync.Models;

namespace DrawSync.Repositories.Interface
{
    public interface IDrawingRepository : IBaseRepository<Drawing>
    {
        Task<IEnumerable<Drawing>> GetByOrganizationAsync(string organizationId);
    }
}
