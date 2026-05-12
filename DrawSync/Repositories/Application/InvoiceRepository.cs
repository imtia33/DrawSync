using DrawSync.Models;
using DrawSync.Repositories.Interface;
using Appwrite;
using Appwrite.Services;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DrawSync.Repositories.Application
{
    public class InvoiceRepository : BaseRepository<Invoice>, IInvoiceRepository
    {
        public InvoiceRepository(TablesDB tables, IConfiguration configuration)
            : base(tables, configuration, configuration["Appwrite:Tables:Invoice"]!)
        {
        }

        public async Task<IEnumerable<Invoice>> GetByOrganizationAsync(string organizationId)
        {
            var result = await _tables.ListRows(
                databaseId: _databaseId,
                tableId: _tableId,
                queries: new List<string> 
                { 
                    Query.Equal("organizationId", organizationId),
                    Query.OrderDesc("$createdAt"),
                    Query.Limit(100)
                }
            );

            return result.Rows.Select(row => {
                var json = JsonConvert.SerializeObject(row.Data);
                return JsonConvert.DeserializeObject<Invoice>(json)!;
            });
        }
    }
}
