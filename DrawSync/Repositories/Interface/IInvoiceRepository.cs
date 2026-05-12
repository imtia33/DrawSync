using DrawSync.Models;

namespace DrawSync.Repositories.Interface
{
    public interface IInvoiceRepository : IBaseRepository<Invoice>
    {
        Task<IEnumerable<Invoice>> GetByOrganizationAsync(string organizationId);
    }
}
