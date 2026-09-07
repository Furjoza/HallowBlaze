using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const int MaxTokens = 4096;
const int IdleTimeoutSeconds = 180;
const int AbsoluteTimeoutSeconds = 360;
const string DefaultListenUrl = "http://127.0.0.1:11435";
const string DefaultUpstreamUrl = "http://127.0.0.1:11434/v1/chat/completions";

if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = TokenPolicySelfTest.Run();
    return;
}

var runtimeRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "HallowBlaze",
    "LocalAgentHarness");
var logPath = Environment.GetEnvironmentVariable("HALLOWBLAZE_GATEWAY_LOG_PATH")
    ?? Path.Combine(runtimeRoot, "logs", "gateway.jsonl");
var upstreamUrl = Environment.GetEnvironmentVariable("HALLOWBLAZE_GATEWAY_UPSTREAM_URL")
    ?? DefaultUpstreamUrl;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(DefaultListenUrl);
builder.Services.AddHttpClient("ollama")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(15),
        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        AutomaticDecompression = DecompressionMethods.None
    })
    .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);

var app = builder.Build();

app.MapGet("/health", () => Results.Json(new
{
    status = "ok",
    maxTokens = MaxTokens,
    idleTimeoutSeconds = IdleTimeoutSeconds,
    absoluteRequestTimeoutSeconds = AbsoluteTimeoutSeconds,
    upstream = "ollama-loopback"
}));

app.MapPost("/v1/chat/completions", async (HttpContext context, IHttpClientFactory clientFactory) =>
{
    if (context.Connection.RemoteIpAddress is not null
        && !IPAddress.IsLoopback(context.Connection.RemoteIpAddress))
    {
        return Results.StatusCode(StatusCodes.Status403Forbidden);
    }

    var requestId = Guid.NewGuid().ToString("N");
    var stopwatch = Stopwatch.StartNew();
    JsonObject body;

    try
    {
        using var reader = new StreamReader(
            context.Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: false,
            leaveOpen: true);
        body = JsonNode.Parse(await reader.ReadToEndAsync(context.RequestAborted)) as JsonObject
            ?? throw new JsonException("The request body must be a JSON object.");
    }
    catch (Exception exception) when (exception is JsonException or InvalidOperationException)
    {
        GatewayLog.Write(logPath, requestId, "rejected", reason: "invalid-json");
        return Results.BadRequest(new
        {
            error = new { message = exception.Message, type = "invalid_request_error" }
        });
    }

    TokenDecision tokenDecision;
    try
    {
        tokenDecision = TokenPolicy.Normalize(body, MaxTokens);
    }
    catch (JsonException exception)
    {
        GatewayLog.Write(logPath, requestId, "rejected", reason: "invalid-token-limit");
        return Results.BadRequest(new
        {
            error = new { message = exception.Message, type = "invalid_request_error" }
        });
    }

    var model = body["model"]?.GetValue<string>() ?? "unknown";
    var streaming = body["stream"]?.GetValue<bool>() ?? false;
    GatewayLog.Write(
        logPath,
        requestId,
        "started",
        model,
        streaming,
        tokenDecision.RequestedField,
        tokenDecision.RequestedTokens,
        tokenDecision.EffectiveTokens,
        tokenDecision.WasClamped);

    using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, upstreamUrl)
    {
        Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
    };
    using var idleCts = new CancellationTokenSource(TimeSpan.FromSeconds(IdleTimeoutSeconds));
    using var absoluteCts = new CancellationTokenSource(TimeSpan.FromSeconds(AbsoluteTimeoutSeconds));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
        context.RequestAborted,
        idleCts.Token,
        absoluteCts.Token);

    HttpResponseMessage? upstreamResponse = null;
    long responseBytes = 0;

    try
    {
        upstreamResponse = await clientFactory.CreateClient("ollama").SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            linkedCts.Token);

        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        CopyResponseHeaders(upstreamResponse, context.Response);

        await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(linkedCts.Token);
        var buffer = new byte[32 * 1024];
        while (true)
        {
            idleCts.CancelAfter(TimeSpan.FromSeconds(IdleTimeoutSeconds));
            var read = await upstreamStream.ReadAsync(buffer.AsMemory(), linkedCts.Token);
            if (read == 0)
            {
                break;
            }

            responseBytes += read;
            idleCts.CancelAfter(TimeSpan.FromSeconds(IdleTimeoutSeconds));
            await context.Response.Body.WriteAsync(buffer.AsMemory(0, read), linkedCts.Token);
            await context.Response.Body.FlushAsync(linkedCts.Token);
        }

        GatewayLog.Write(
            logPath,
            requestId,
            "completed",
            status: (int)upstreamResponse.StatusCode,
            elapsedMs: stopwatch.ElapsedMilliseconds,
            responseBytes: responseBytes);
        return Results.Empty;
    }
    catch (OperationCanceledException)
    {
        var reason = context.RequestAborted.IsCancellationRequested
            ? "client-cancel"
            : absoluteCts.IsCancellationRequested
                ? "absolute-request-timeout"
                : "no-progress-timeout";

        GatewayLog.Write(
            logPath,
            requestId,
            "cancelled",
            reason: reason,
            elapsedMs: stopwatch.ElapsedMilliseconds,
            responseBytes: responseBytes);

        if (context.Response.HasStarted)
        {
            context.Abort();
            return Results.Empty;
        }

        return Results.Json(
            new { error = new { message = $"Gateway stopped the request: {reason}.", type = "timeout_error" } },
            statusCode: reason == "client-cancel" ? 499 : StatusCodes.Status504GatewayTimeout);
    }
    catch (HttpRequestException exception)
    {
        GatewayLog.Write(
            logPath,
            requestId,
            "upstream-error",
            reason: exception.GetType().Name,
            elapsedMs: stopwatch.ElapsedMilliseconds);
        return Results.Json(
            new { error = new { message = "The local Ollama upstream is unavailable.", type = "upstream_error" } },
            statusCode: StatusCodes.Status502BadGateway);
    }
    finally
    {
        upstreamResponse?.Dispose();
    }
});

app.Run();

static void CopyResponseHeaders(HttpResponseMessage source, HttpResponse destination)
{
    foreach (var header in source.Headers.Concat(source.Content.Headers))
    {
        if (header.Key.Equals("transfer-encoding", StringComparison.OrdinalIgnoreCase)
            || header.Key.Equals("connection", StringComparison.OrdinalIgnoreCase)
            || header.Key.Equals("content-length", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        destination.Headers[header.Key] = header.Value.ToArray();
    }
}

internal sealed record TokenDecision(
    string RequestedField,
    int RequestedTokens,
    int EffectiveTokens,
    bool WasClamped);

internal static class TokenPolicy
{
    private static readonly string[] SupportedFields =
    [
        "max_tokens",
        "max_completion_tokens",
        "max_output_tokens"
    ];

    public static TokenDecision Normalize(JsonObject body, int hardCap)
    {
        var present = SupportedFields.Where(body.ContainsKey).ToArray();
        var requestedField = present.FirstOrDefault() ?? "gateway-default";
        var requestedTokens = hardCap;

        if (present.Length > 0)
        {
            var node = body[requestedField];
            if (node is not JsonValue value
                || !value.TryGetValue<int>(out requestedTokens)
                || requestedTokens <= 0)
            {
                throw new JsonException($"{requestedField} must be a positive integer.");
            }
        }

        var effectiveTokens = Math.Min(requestedTokens, hardCap);
        foreach (var field in SupportedFields)
        {
            body.Remove(field);
        }

        body["max_tokens"] = effectiveTokens;
        return new TokenDecision(
            requestedField,
            requestedTokens,
            effectiveTokens,
            requestedTokens > effectiveTokens);
    }
}

internal static class GatewayLog
{
    private static readonly object Sync = new();

    public static void Write(
        string path,
        string requestId,
        string eventName,
        string? model = null,
        bool? streaming = null,
        string? requestedField = null,
        int? requestedTokens = null,
        int? effectiveTokens = null,
        bool? wasClamped = null,
        int? status = null,
        string? reason = null,
        long? elapsedMs = null,
        long? responseBytes = null)
    {
        var metadata = new Dictionary<string, object?>
        {
            ["timestampUtc"] = DateTimeOffset.UtcNow,
            ["requestId"] = requestId,
            ["eventName"] = eventName,
            ["model"] = model,
            ["streaming"] = streaming,
            ["requestedField"] = requestedField,
            ["requestedTokens"] = requestedTokens,
            ["effectiveTokens"] = effectiveTokens,
            ["wasClamped"] = wasClamped,
            ["status"] = status,
            ["reason"] = reason,
            ["elapsedMs"] = elapsedMs,
            ["responseBytes"] = responseBytes
        };
        var line = JsonSerializer.Serialize(metadata.Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value));

        lock (Sync)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}

internal static class TokenPolicySelfTest
{
    public static int Run()
    {
        var failures = new List<string>();
        Check("default", "{}", "gateway-default", 4096, false, failures);
        Check("max_tokens", "{\"max_tokens\":64}", "max_tokens", 64, false, failures);
        Check("max_completion_tokens", "{\"max_completion_tokens\":128}", "max_completion_tokens", 128, false, failures);
        Check("max_output_tokens", "{\"max_output_tokens\":256}", "max_output_tokens", 256, false, failures);
        Check("clamp", "{\"max_completion_tokens\":99999}", "max_completion_tokens", 4096, true, failures);

        try
        {
            TokenPolicy.Normalize(JsonNode.Parse("{\"max_tokens\":\"many\"}")!.AsObject(), 4096);
            failures.Add("invalid-value was accepted");
        }
        catch (JsonException)
        {
        }

        if (failures.Count == 0)
        {
            Console.WriteLine("Gateway token policy self-test: PASS (6/6)");
            return 0;
        }

        foreach (var failure in failures)
        {
            Console.Error.WriteLine(failure);
        }
        Console.Error.WriteLine($"Gateway token policy self-test: FAIL ({failures.Count} failure(s))");
        return 1;
    }

    private static void Check(
        string name,
        string json,
        string expectedField,
        int expectedTokens,
        bool expectedClamped,
        ICollection<string> failures)
    {
        var body = JsonNode.Parse(json)!.AsObject();
        var decision = TokenPolicy.Normalize(body, 4096);
        var remainingLimitFields = body.Select(pair => pair.Key)
            .Where(key => key is "max_completion_tokens" or "max_output_tokens")
            .ToArray();

        if (decision.RequestedField != expectedField
            || decision.EffectiveTokens != expectedTokens
            || decision.WasClamped != expectedClamped
            || body["max_tokens"]?.GetValue<int>() != expectedTokens
            || remainingLimitFields.Length != 0)
        {
            failures.Add($"{name} failed");
        }
    }
}