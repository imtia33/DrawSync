using System.Data.Common;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace DrawSync.Data
{
    public class SessionContextInterceptor : DbConnectionInterceptor
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public SessionContextInterceptor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            SetSessionContext(connection);
            base.ConnectionOpened(connection, eventData);
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await SetSessionContextAsync(connection, cancellationToken);
            await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        }

        private void SetSessionContext(DbConnection connection)
        {
            var userId = GetUserId();
            if (userId != null)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "EXEC sp_set_session_context @key = N'UserId', @value = @UserId;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@UserId";
                parameter.Value = userId;
                command.Parameters.Add(parameter);
                command.ExecuteNonQuery();
            }
        }

        private async Task SetSessionContextAsync(DbConnection connection, CancellationToken cancellationToken)
        {
            var userId = GetUserId();
            if (userId != null)
            {
                using var command = connection.CreateCommand();
                command.CommandText = "EXEC sp_set_session_context @key = N'UserId', @value = @UserId;";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@UserId";
                parameter.Value = userId;
                command.Parameters.Add(parameter);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        private int? GetUserId()
        {
            var userIdClaim = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }
            return null;
        }
    }
}
