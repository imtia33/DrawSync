using DrawSync.Repositories.Interface;
using DrawSync.UnitOfWork.Interface;
using System.Threading.Tasks;

namespace DrawSync.UnitOfWork.Application
{
    public class UnitOfWork : IUnitOfWork
    {
        public UnitOfWork(
            IUserRepository userRepository,
            Repositories.Interface.IOrganizationRepository organizationRepository,
            Repositories.Interface.IDrawingRepository drawingRepository,
            Repositories.Interface.IInvoiceRepository invoiceRepository,
            Repositories.Interface.IUsageRepository usageRepository)
        {
            Users = userRepository;
            Organizations = organizationRepository;
            Drawings = drawingRepository;
            Invoices = invoiceRepository;
            Usage = usageRepository;
        }

        public IUserRepository Users { get; }
        public Repositories.Interface.IOrganizationRepository Organizations { get; }
        public Repositories.Interface.IDrawingRepository Drawings { get; }
        public Repositories.Interface.IInvoiceRepository Invoices { get; }
        public Repositories.Interface.IUsageRepository Usage { get; }

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
