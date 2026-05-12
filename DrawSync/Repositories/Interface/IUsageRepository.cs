using DrawSync.Models;

namespace DrawSync.Repositories.Interface
{
    public interface IUsageRepository : IBaseRepository<Usage>
    {
        Task<Usage?> GetCurrentMonthAsync(string organizationId);
    }
}
