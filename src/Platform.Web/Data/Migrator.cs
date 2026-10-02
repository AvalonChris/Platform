using Dapper;
using Npgsql;

namespace Platform.Web.Data;

public static class Migrator
{
    /// <summary>Applies every .sql file in the directory that has not been applied yet, in file-name order.</summary>
    public static async Task MigrateAsync(NpgsqlDataSource dataSource, string directory, ILogger logger)
    {
        await using var connection = await dataSource.OpenConnectionAsync();

        await connection.ExecuteAsync(
            """
            create table if not exists schema_migrations (
                name       text primary key,
                applied_at timestamptz not null default now()
            )
            """);

        var applied = (await connection.QueryAsync<string>("select name from schema_migrations")).ToHashSet();
        var files = Directory.GetFiles(directory, "*.sql").OrderBy(Path.GetFileName, StringComparer.Ordinal);

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (applied.Contains(name))
                continue;

            await using var transaction = await connection.BeginTransactionAsync();
            await connection.ExecuteAsync(await File.ReadAllTextAsync(file), transaction: transaction);
            await connection.ExecuteAsync(
                "insert into schema_migrations (name) values (@name)", new { name }, transaction);
            await transaction.CommitAsync();

            logger.LogInformation("Applied migration {Migration}", name);
        }
    }

    public static async Task RunScriptAsync(NpgsqlDataSource dataSource, string file, ILogger logger)
    {
        await using var connection = await dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(await File.ReadAllTextAsync(file));
        logger.LogInformation("Ran script {Script}", Path.GetFileName(file));
    }
}
