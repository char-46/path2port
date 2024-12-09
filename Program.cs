using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Primitives;

var builder = WebApplication.CreateBuilder(args);
// Configure the application to listen on specific URLs
builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Listen(System.Net.IPAddress.Any, 5000); // HTTP
    // options.Listen(System.Net.IPAddress.Any, 5002, listenOptions =>
    // {
    //     listenOptions.UseHttps(); // HTTPS configuration if needed
    // });
});
// Add YARP to the service collection
builder.Services.AddReverseProxy()
    .LoadFromMemory(GetRoutes(), GetClusters())
    .AddTransforms(builderContext =>
    {
        builderContext.AddRequestTransform(transformContext =>
        {
            transformContext.ProxyRequest.Version = HttpVersion.Version11;
            return ValueTask.CompletedTask;
        });
    });

var app = builder.Build();
app.MapReverseProxy();

// Middleware to handle redirection and maintain the path info
app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        Console.WriteLine($"Hit! {context.Response.StatusCode}");
        if (context.Response.StatusCode >= 300 && context.Response.StatusCode < 400)
        {
            if (context.Response.Headers.TryGetValue("Location", out StringValues location))
            {
                // Modify the Location header to maintain the original path structure
                var originalPath = context.Request.Path.ToString();

                Console.WriteLine(context.Request.Path);

                if (!string.IsNullOrEmpty(originalPath) && location.ToString().StartsWith("/"))
                {
                    context.Response.Headers["Location"] = originalPath + location.ToString();
                }
            }
        }
        return Task.CompletedTask;
    });

    await next.Invoke();
});


app.Run();

RouteConfig[] GetRoutes()
{
    var routes = new List<RouteConfig>();
    for (int i = 1; i <= 100; i++)
    {
        routes.Add(new RouteConfig
        {
            RouteId = $"route{i}",
            Match = new RouteMatch
            {
                Path = $"/{i}/{{**catch-all}}"
            },
            Transforms = new List<IReadOnlyDictionary<string, string>>
            {
                new Dictionary<string, string>
                {
                    { "PathRemovePrefix", $"/{i}" }
                }
            },
            ClusterId = $"cluster{i}"
        });
    }
    return routes.ToArray();
}

ClusterConfig[] GetClusters()
{
    var clusters = new List<ClusterConfig>();
    for (int i = 1; i <= 100; i++)
    {
        clusters.Add(new ClusterConfig
        {
            ClusterId = $"cluster{i}",
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                { $"destination{i}", new DestinationConfig { Address = $"http://localhost:{6098 + i}" } }
            }
        });
    }
    return clusters.ToArray();
}
