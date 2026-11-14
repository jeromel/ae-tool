using AeTool.Api.Auth;
using AeTool.Api.Endpoints;
using AeTool.Api.Sources;

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
        builder.Services.AddKeyStorage(paths);
        builder.AddAeAuthentication(paths);
        builder.Services.AddSources(paths);
        builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = 100 * 1024 * 1024);
        builder.AddProxySupport();

        var app = builder.Build();
        // En premier : le reste de la chaîne doit voir l'adresse et le protocole d'origine quand un mandataire est devant.
        app.UseHosting(paths);
        // Avant les fichiers statiques : les en-têtes de sécurité couvrent aussi l'interface ; les habilitations ne visent que /api.
        app.UseAeAuthentication();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Tout ce que fait l'interface passe par les routes de /api, regroupées par domaine dans Endpoints/.
        var api = app.MapGroup("/api");
        Hosting.MapHealth(api);
        AuthEndpoints.Map(api);
        UserEndpoints.Map(api);
        SummaryEndpoints.Map(api);
        MetamodelEndpoints.Map(api);
        ObjectEndpoints.Map(api);
        EditingEndpoints.Map(api);
        ViewEndpoints.Map(api);
        AnalysisEndpoints.Map(api);
        SpaceEndpoints.Map(api);
        ProjectEndpoints.Map(api);
        PortfolioEndpoints.Map(api);
        ExchangeEndpoints.Map(api);
        SourceEndpoints.Map(api);
        if (app.Environment.IsDevelopment()) AdminEndpoints.MapDevelopment(api);

        app.MapFallbackToFile("index.html");
        return app;
    }
}
