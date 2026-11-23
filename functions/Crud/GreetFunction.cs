using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace SwallowKit.Functions;

public sealed class GreetFunction
{
    [Function("greet")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "greet")] HttpRequestData request)
    {
        var query = request.Url.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        var name = "SwallowKit";
        foreach (var segment in query)
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && parts[0] == "name")
            {
                name = Uri.UnescapeDataString(parts[1]);
                break;
            }
        }
        var response = request.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new
        {
            message = $"Hello, {name}! This message is from Azure Functions.",
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
        });
        return response;
    }
}
