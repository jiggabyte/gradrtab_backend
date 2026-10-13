namespace GradrTab.Configuration;

// CORS origins for browser clients (the Vercel frontend, local dev servers,
// Swagger UI on another host, ...). Bound from the "Cors" section of
// appsettings; every value can also come from an environment variable:
// Cors__AllowedOrigins, Cors__AllowAnyOrigin.
public class CorsOptions
{
    public const string SectionName = "Cors";

    // Exact origins allowed next to the permissive fallback, e.g.
    // ["https://gradr-tab-frontend.vercel.app", "http://localhost:5173"].
    public List<string> AllowedOrigins { get; set; } = [];

    // True (the default) lets every client reach the API: the policy answers
    // any Origin with AllowAnyOrigin + AllowAnyHeader + AllowAnyMethod.
    // Set false to restrict browsers to AllowedOrigins only.
    public bool AllowAnyOrigin { get; set; } = true;
}
