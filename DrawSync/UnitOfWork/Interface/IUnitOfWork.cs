using DrawSync.Repositories.Interface;
using System;
using System.Threading.Tasks;

namespace DrawSync.UnitOfWork.Interface
{
    public interface IUnitOfWork : IDisposable
    {
        IUserRepository Users { get; }
        Repositories.Interface.IOrganizationRepository Organizations { get; }
        Repositories.Interface.IDrawingRepository Drawings { get; }
        Repositories.Interface.IInvoiceRepository Invoices { get; }
        Repositories.Interface.IUsageRepository Usage { get; }

        Task SaveChangesAsync();
    }
}
