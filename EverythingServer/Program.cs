using EverythingServer;
using EverythingServer.Prompts;
using EverythingServer.Resources;
using EverythingServer.Tools;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.Collections.Concurrent;

var builder = WebApplication.CreateBuilder(args);

var mcpSettings = builder.Configuration.GetSection("Mcp").Get<McpServerSettings>() ?? new McpServerSettings();
var isStateless = mcpSettings.Stateless;

// Dictionary of session IDs to a set of resource URIs they are subscribed to
// The value is a ConcurrentDictionary used as a thread-safe HashSet
// because .NET does not have a built-in concurrent HashSet
ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> subscriptions = new();

var mcpBuilder = builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.Stateless = isStateless;
        // Add a RunSessionHandler to remove all subscriptions for the session when it ends
        options.RunSessionHandler = async (httpContext, mcpServer, token) =>
        {
            if (mcpServer.SessionId == null)
            {
                // There is no sessionId if the serverOptions.Stateless is true
                await mcpServer.RunAsync(token);
                return;
            }
            try
            {
                subscriptions[mcpServer.SessionId] = new ConcurrentDictionary<string, byte>();
                // Start an instance of SubscriptionMessageSender for this session
                using var subscriptionSender = new SubscriptionMessageSender(mcpServer, subscriptions[mcpServer.SessionId]);
                await subscriptionSender.StartAsync(token);
                // Start an instance of LoggingUpdateMessageSender for this session
                using var loggingSender = new LoggingUpdateMessageSender(mcpServer);
                await loggingSender.StartAsync(token);
                await mcpServer.RunAsync(token);
            }
            finally
            {
                // This code runs when the session ends
                subscriptions.TryRemove(mcpServer.SessionId, out _);
            }
        };
    })
    .WithTools<AddTool>()
    .WithTools<AnnotatedMessageTool>()
    .WithTools<EchoTool>()
    .WithTools<LongRunningTool>()
    .WithTools<PrintEnvTool>()
    .WithTools<SampleLlmTool>()
    .WithTools<TinyImageTool>()
    .WithTools<WeatherTool>()
    .WithPrompts<ComplexPromptType>()
    .WithPrompts<SimplePromptType>()
    .WithResources<SimpleResourceType>();

if (!isStateless)
{
    mcpBuilder = mcpBuilder
        .WithSubscribeToResourcesHandler(async (ctx, ct) =>
        {
            if (ctx.Server.SessionId == null)
            {
                throw new McpException("Cannot add subscription for server with null SessionId");
            }
            if (ctx.Params?.Uri is { } uri)
            {
                subscriptions[ctx.Server.SessionId].TryAdd(uri, 0);

                await ctx.Server.SampleAsync([
                    new ChatMessage(ChatRole.System, "You are a helpful test server"),
                    new ChatMessage(ChatRole.User, $"Resource {uri}, context: A new subscription was started"),
                ],
                options: new ChatOptions
                {
                    MaxOutputTokens = 100,
                    Temperature = 0.7f,
                },
                cancellationToken: ct);
            }

            return new EmptyResult();
        })
        .WithUnsubscribeFromResourcesHandler(async (ctx, ct) =>
        {
            if (ctx.Server.SessionId == null)
            {
                throw new McpException("Cannot remove subscription for server with null SessionId");
            }
            if (ctx.Params?.Uri is { } uri)
            {
                subscriptions[ctx.Server.SessionId].TryRemove(uri, out _);
            }
            return new EmptyResult();
        });
}

mcpBuilder = mcpBuilder
    .WithCompleteHandler(async (ctx, ct) =>
    {
        var exampleCompletions = new Dictionary<string, IEnumerable<string>>
        {
            { "style", ["casual", "formal", "technical", "friendly"] },
            { "temperature", ["0", "0.5", "0.7", "1.0"] },
            { "resourceId", ["1", "2", "3", "4", "5"] }
        };

        if (ctx.Params is not { } @params)
        {
            throw new NotSupportedException($"Params are required.");
        }

        var @ref = @params.Ref;
        var argument = @params.Argument;

        if (@ref is ResourceTemplateReference rtr)
        {
            var resourceId = rtr.Uri?.Split("/").Last();

            if (resourceId is null)
            {
                return new CompleteResult();
            }

            var values = exampleCompletions["resourceId"].Where(id => id.StartsWith(argument.Value));

            return new CompleteResult
            {
                Completion = new Completion { Values = [.. values], HasMore = false, Total = values.Count() }
            };
        }

        if (@ref is PromptReference pr)
        {
            if (!exampleCompletions.TryGetValue(argument.Name, out IEnumerable<string>? value))
            {
                throw new NotSupportedException($"Unknown argument name: {argument.Name}");
            }

            var values = value.Where(value => value.StartsWith(argument.Value));
            return new CompleteResult
            {
                Completion = new Completion { Values = [.. values], HasMore = false, Total = values.Count() }
            };
        }

        throw new NotSupportedException($"Unknown reference type: {@ref.Type}");
    })
    .WithSetLoggingLevelHandler(async (ctx, ct) =>
    {
        if (ctx.Params?.Level is null)
        {
            throw new McpProtocolException("Missing required argument 'level'", McpErrorCode.InvalidParams);
        }

        // The SDK updates the LoggingLevel field of the IMcpServer

        await ctx.Server.SendNotificationAsync("notifications/message", new
        {
            Level = "debug",
            Logger = "test-server",
            Data = $"Logging level set to {ctx.Params.Level}",
        }, cancellationToken: ct);

        return new EmptyResult();
    });

ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService("everything-server");
builder.Services.AddOpenTelemetry()
    .WithTracing(b => b.AddSource("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithMetrics(b => b.AddMeter("*").AddHttpClientInstrumentation().SetResourceBuilder(resource))
    .WithLogging(b => b.SetResourceBuilder(resource))
    .UseOtlpExporter();

var app = builder.Build();

// 健康檢查端點
app.MapGet("/health", (HttpContext ctx) =>
{
    Console.WriteLine($"[IN] /health {DateTime.Now:HH:mm:ss} from {ctx.Connection.RemoteIpAddress}");
    return Results.Text("OK");
});

// Bearer Token 驗證中介層
app.Use(async (ctx, next) =>
{
    // 健康檢查端點不需要驗證
    if (ctx.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    if (!ctx.Request.Headers.TryGetValue("Authorization", out var authHeader) ||
        !authHeader.ToString().Equals("Bearer usr_11100000000000000000"))
    {
        ctx.Response.StatusCode = 401; // Unauthorized
        var url = ctx.Request.GetDisplayUrl();
        var logMessage = $"[{DateTime.Now:HH:mm:ss}] Unauthorized request to {url}. Missing or invalid token.";
        Console.WriteLine(logMessage);
        await ctx.Response.WriteAsync("Unauthorized");
        return;
    }

    await next();
});


// 全域中介層：記錄所有 Request 和 Response
app.Use(async (ctx, next) =>
{
    // ===== 1. 記錄 Request =====
    var url = ctx.Request.GetDisplayUrl();
    var hasBodyHeader = ctx.Request.Headers.ContainsKey("Content-Length")
                        || ctx.Request.Headers.ContainsKey("Transfer-Encoding");

    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {ctx.Request.Method} {url}");
    Console.WriteLine($"  Accept: {ctx.Request.Headers["Accept"]}");
    Console.WriteLine($"  Content-Length: {ctx.Request.ContentLength?.ToString() ?? "(null)"}  HasBodyHeader={hasBodyHeader}");
    Console.WriteLine($"  MCP-Protocol-Version: {ctx.Request.Headers["MCP-Protocol-Version"]}");
    Console.WriteLine($"  Mcp-Session-Id: {ctx.Request.Headers["Mcp-Session-Id"]}");

    // ===== 2. 暫存原本 Response Body =====
    var originalBodyStream = ctx.Response.Body;

    await using var responseBody = new MemoryStream();
    ctx.Response.Body = responseBody;

    // ===== 3. 呼叫下一個中介層 =====
    await next();

    // ===== 4. 讀取 Response Body =====
    ctx.Response.Body.Seek(0, SeekOrigin.Begin);
    string responseText = await new StreamReader(ctx.Response.Body).ReadToEndAsync();
    ctx.Response.Body.Seek(0, SeekOrigin.Begin);

    // ===== 5. 記錄 Response =====
    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] RESPONSE {ctx.Response.StatusCode}");
    string mcpsid = ctx.Response.Headers["Mcp-Session-Id"].ToString();
    if(!string.IsNullOrEmpty(mcpsid))
    {
        Console.WriteLine($"Mcp-Session-Id: {mcpsid}");
    }
    Console.WriteLine(responseText);

    // ===== 6. 把 Response 寫回給客戶端 =====
    await responseBody.CopyToAsync(originalBodyStream);
});
app.MapMcp();

app.Run();
