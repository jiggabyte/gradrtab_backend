namespace GradrTab.Configuration;

/// <summary>
/// Resolves the Postgres connection string from environment variables.
/// Priority: DATABASE_URL (Render) > discrete DB_* vars > ConnectionStrings__DefaultConnection > appsettings.
/// </summary>
public static class DbConnectionResolver
{
    public static string? Resolve(IConfiguration config)
    {
        // 1. Render-style DATABASE_URL: postgres://user:pass@host:port/db?sslmode=require
        var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
        if (!string.IsNullOrWhiteSpace(databaseUrl))
        {
            return ConvertDatabaseUrlToNpgsql(databaseUrl.Trim());
        }

        // 2. Discrete DB_* vars (works nicely with a hand-written .env file)
        var dbHost = Environment.GetEnvironmentVariable("DB_HOST");
        var dbName = Environment.GetEnvironmentVariable("DB_NAME")
            ?? Environment.GetEnvironmentVariable("DB_DATABASE");
        var dbUser = Environment.GetEnvironmentVariable("DB_USER")
            ?? Environment.GetEnvironmentVariable("DB_USERNAME");
        var dbPassword = Environment.GetEnvironmentVariable("DB_PASSWORD");
        if (!string.IsNullOrWhiteSpace(dbHost) && !string.IsNullOrWhiteSpace(dbName))
        {
            var dbPort = Environment.GetEnvironmentVariable("DB_PORT") ?? "5432";
            var sslMode = Environment.GetEnvironmentVariable("DB_SSLMODE") ?? "Require";
            return $"Host={dbHost};Port={dbPort};Database={dbName};Username={dbUser};Password={dbPassword};Ssl Mode={sslMode};Trust Server Certificate=true";
        }

        // 3. Standard .NET env override (ConnectionStrings__DefaultConnection) or appsettings.json
        return config.GetConnectionString("DefaultConnection");
    }

    private static string ConvertDatabaseUrlToNpgsql(string databaseUrl)
    {
        // Npgsql cannot consume a URL directly, translate it to a key=value connection string.
        var uri = new Uri(databaseUrl);
        var userInfo = uri.UserInfo.Split(':', 2);
        var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : string.Empty;
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.TrimStart('/');
        var port = uri.Port > 0 ? uri.Port : 5432;

        // Preserve ?sslmode=require when present, otherwise force TLS (Render requires it).
        var sslMode = "Require";
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Equals("sslmode", StringComparison.OrdinalIgnoreCase))
            {
                sslMode = kv[1];
            }
        }

        return $"Host={uri.Host};Port={port};Database={database};Username={username};Password={password};Ssl Mode={sslMode};Trust Server Certificate=true";
    }
}
