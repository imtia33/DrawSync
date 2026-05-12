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
    public class DrawingRepository : BaseRepository<Drawing>, IDrawingRepository
    {
        public DrawingRepository(TablesDB tables, IConfiguration configuration)
            : base(tables, configuration, configuration["Appwrite:Tables:Drawing"]!)
        {
        }

        public async Task<IEnumerable<Drawing>> GetByOrganizationAsync(string organizationId)
        {
            var result = await _tables.ListRows(
                databaseId: _databaseId,
                tableId: _tableId,
                queries: new List<string> { Query.Equal("organizationId", organizationId) }
            );

            return result.Rows.Select(row => {
                var json = JsonConvert.SerializeObject(row.Data);
                return JsonConvert.DeserializeObject<Drawing>(json)!;
            });
        }
    }
}
