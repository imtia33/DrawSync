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
    public class UserRepository : BaseRepository<User>, IUserRepository
    {
        public UserRepository(TablesDB tables, IConfiguration configuration) 
            : base(tables, configuration, configuration["Appwrite:Tables:Users"]!)
        {
        }

        public async Task<User?> GetByEmailAsync(string email)
        {
            var result = await _tables.ListRows(
                databaseId: _databaseId,
                tableId: _tableId,
                queries: new List<string> { Query.Equal("email", email) }
            );

            var row = result.Rows.FirstOrDefault();
            if (row == null || row.Data == null) return null;

            var json = JsonConvert.SerializeObject(row.Data);
            return JsonConvert.DeserializeObject<User>(json);
        }

        public async Task<User?> GetProfileAsync(string id)
        {
            return await GetByIdAsync(id);
        }
    }
}
