namespace TestApp.Endpoints;

public static class ShutdownEndpoint
{
    public static void MapShutdownEndpoint(this WebApplication app)
    {
        app.MapPost("/shutdown", (IHostApplicationLifetime lifetime) =>
        {
            lifetime.StopApplication();
            return Results.Json(new { stopping = true }, statusCode: StatusCodes.Status202Accepted);
        }).WithTags("shutdown");
    }
}
