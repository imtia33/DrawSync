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
    public class UsageRepository : BaseRepository<Usage>, IUsageRepository
    {
        public UsageRepository(TablesDB tables, IConfiguration configuration)
            : base(tables, configuration, configuration["Appwrite:Tables:Usage"]!)
        {
        }

        public async Task<Usage?> GetCurrentMonthAsync(string organizationId)
        {
            var result = await _tables.ListRows(
                databaseId: _databaseId,
                tableId: _tableId,
                queries: new List<string> 
                { 
                    Query.Equal("organizationId", organizationId)
                }
            );

            var row = result.Rows.FirstOrDefault();
            if (row == null || row.Data == null) return null;

            var json = JsonConvert.SerializeObject(row.Data);
            return JsonConvert.DeserializeObject<Usage>(json);
        }
    }
}
