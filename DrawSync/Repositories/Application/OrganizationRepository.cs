using DrawSync.Models;
using DrawSync.Repositories.Interface;
using Appwrite;
using Appwrite.Services;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Organization = DrawSync.Models.Organization;

namespace DrawSync.Repositories.Application
{
    public class OrganizationRepository : BaseRepository<Organization>, IOrganizationRepository
    {
        public OrganizationRepository(TablesDB tables, IConfiguration configuration)
            : base(tables, configuration, configuration["Appwrite:Tables:Organization"]!)
        {
        }

        public async Task<Organization?> GetByNameAsync(string name)
        {
            var result = await _tables.ListRows(
                databaseId: _databaseId,
                tableId: _tableId,
                queries: new List<string> { Query.Equal("name", name) }
            );

            var row = result.Rows.FirstOrDefault();
            if (row == null || row.Data == null) return null;

            var json = JsonConvert.SerializeObject(row.Data);
            return JsonConvert.DeserializeObject<Organization>(json);
        }

        public async Task<IEnumerable<Organization>> GetByIdsAsync(IEnumerable<string> ids)
        {
            if (ids == null || !ids.Any())
            {
                return Enumerable.Empty<Organization>();
            }

            var result = await _tables.ListRows(
                databaseId: _databaseId,
                tableId: _tableId,
                queries: new List<string> { Query.Equal("$id", ids.ToList()) }
            );

            return result.Rows.Select(row => {
                var json = JsonConvert.SerializeObject(row.Data);
                return JsonConvert.DeserializeObject<Organization>(json)!;
            });
        }
    }
}
