using System.Collections.Generic;
using System.Threading.Tasks;

namespace DrawSync.Repositories.Interface
{
    public interface IBaseRepository<T> where T : class
    {
        Task<IEnumerable<T>> GetAllAsync();
        Task<T?> GetByIdAsync(string id);
        Task AddAsync(T entity, List<string>? permissions = null);
        Task UpdateAsync(string id, T entity);
        Task DeleteAsync(string id);
    }
}
