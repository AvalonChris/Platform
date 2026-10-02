using Dapper;
using Npgsql;
using Platform.Web.Areas.Admin.Models;

namespace Platform.Web.Areas.Admin.Data;

public class AdminUserRepository(NpgsqlDataSource dataSource)
{
    public async Task<AdminUser?> GetByEmailAsync(string email)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        return await connection.QuerySingleOrDefaultAsync<AdminUser>(
            "select id, email, password_hash from admin_users where lower(email) = lower(@email)", new { email });
    }

    public async Task SaveAsync(string email, string passwordHash)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(
            """
            insert into admin_users (email, password_hash) values (@email, @passwordHash)
            on conflict (lower(email)) do update set password_hash = excluded.password_hash
            """,
            new { email, passwordHash });
    }
}
