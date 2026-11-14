using AeTool.Api.Endpoints;

namespace AeTool.Api;

/// <summary>
/// Composition de l'application : services, chaîne de traitement HTTP et routes.
/// Séparée de Program.cs pour que les tests d'intégration démarrent la même application, en mémoire, sur un port libre.
/// </summary>
public static class AppHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var dataPath = builder.Configuration["DataPath"] ?? Path.Combine(AppContext.BaseDirectory, "data", "referentiel.json");
        var paths = new DataPaths(dataPath);
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<IRepositoryStore>(new JsonFileStore(paths.RepositoryFile));
        builder.Services.AddSingleton<Workspace>();
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 100 * 1024 * 1024);

        var app = builder.Build();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Tout ce que fait l'interface passe par les routes de /api, regroupées par domaine dans Endpoints/.
        var api = app.MapGroup("/api");
        SummaryEndpoints.Map(api);
        MetamodelEndpoints.Map(api);
        ObjectEndpoints.Map(api);
        ViewEndpoints.Map(api);
        AnalysisEndpoints.Map(api);
        SpaceEndpoints.Map(api);
        ProjectEndpoints.Map(api);
        PortfolioEndpoints.Map(api);
        ExchangeEndpoints.Map(api);
        if (app.Environment.IsDevelopment()) AdminEndpoints.MapDevelopment(api);

        app.MapFallbackToFile("index.html");
        return app;
    }
}
