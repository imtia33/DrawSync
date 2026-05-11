using Appwrite;
using Appwrite.Services;
using Appwrite.Models;
using DrawSync.Repositories.Interface;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DrawSync.Repositories.Application
{
    public class BaseRepository<T> : IBaseRepository<T> where T : class
    {
        protected readonly TablesDB _tables;
        protected readonly string _databaseId;
        protected readonly string _tableId;

        public BaseRepository(TablesDB tables, IConfiguration configuration, string tableId)
        {
            _tables = tables;
            _databaseId = configuration["Appwrite:DatabaseId"]!;
            _tableId = tableId;
        }

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            var result = await _tables.ListRows(
                databaseId: _databaseId,
                tableId: _tableId
            );
            return result.Rows.Select(row => {
                var json = JsonConvert.SerializeObject(row.Data);
                return JsonConvert.DeserializeObject<T>(json)!;
            });
        }

        public async Task<T?> GetByIdAsync(string id)
        {
            try
            {
                var result = await _tables.GetRow(
                    databaseId: _databaseId,
                    tableId: _tableId,
                    rowId: id
                );
                
                if (result == null || result.Data == null)
                {
                    return null;
                }

                var json = JsonConvert.SerializeObject(result.Data);
                
                var settings = new JsonSerializerSettings 
                { 
                    NullValueHandling = NullValueHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore
                };
                
                return JsonConvert.DeserializeObject<T>(json, settings);
            }
            catch (AppwriteException)
            {
                return null;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        public async Task AddAsync(T entity)
        {
            var json = JsonConvert.SerializeObject(entity);
            var createData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;
            
            var rowId = (entity as dynamic).Id ?? "unique()";
            
            createData.Remove("$id");
            createData.Remove("$createdAt");
            createData.Remove("$updatedAt");
            
            await _tables.CreateRow(
                databaseId: _databaseId,
                tableId: _tableId,
                rowId: rowId,
                data: createData
            );
        }

        public async Task UpdateAsync(string id, T entity)
        {
            var json = JsonConvert.SerializeObject(entity);
            var updateData = JsonConvert.DeserializeObject<Dictionary<string, object>>(json)!;
            
            updateData.Remove("$id");
            updateData.Remove("$createdAt");
            updateData.Remove("$updatedAt");

            await _tables.UpdateRow(
                databaseId: _databaseId,
                tableId: _tableId,
                rowId: id,
                data: updateData
            );
        }

        public async Task DeleteAsync(string id)
        {
            await _tables.DeleteRow(
                databaseId: _databaseId,
                tableId: _tableId,
                rowId: id
            );
        }
    }
}