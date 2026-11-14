using System.Text;
using System.Text.Json;
using AeTool.Connectors;
using AeTool.Core;
using static AeTool.Api.Http;

namespace AeTool.Api.Endpoints;

/// <summary>Espaces de lecture par couche.</summary>
public static class SpaceEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet("/spaces/capabilities", (Workspace ws) => ws.Read((repo, an) => new SpaceReader(repo, an).CapabilityMap()));
        api.MapGet("/spaces/applications", (Workspace ws) => ws.Read((repo, an) => new SpaceReader(repo, an).Applications()));
        api.MapGet("/spaces/flows", (Workspace ws) => ws.Read((repo, an) => new SpaceReader(repo, an).Flows()));
        api.MapGet("/spaces/data", (Workspace ws) => ws.Read((repo, an) => new SpaceReader(repo, an).Data()));
        api.MapGet("/spaces/infrastructure", (Workspace ws) => ws.Read((repo, an) => new SpaceReader(repo, an).Infrastructure()));
    }
}
