using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GradrTab.Configuration;
using GradrTab.DTOs;
using Microsoft.Extensions.Options;

namespace GradrTab.Services;

// Posts a chat completion request to an OpenRouter compatible endpoint.
// The API key is read from Ai:ApiKey and falls back to the OPENROUTER_API_KEY
// environment variable, so the secret never has to live in appsettings.
public sealed class OpenRouterAiService : IAiService
{
    // Name of the typed client registered in Program.cs
    public const string HttpClientName = "openrouter";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiOptions _options;
    private readonly ILogger<OpenRouterAiService> _logger;

    public OpenRouterAiService(
        IHttpClientFactory httpClientFactory,
        IOptions<AiOptions> options,
        ILogger<OpenRouterAiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public string Model => _options.Model;

    public bool IsConfigured => ResolveApiKey() is not null;

    public string? UnavailableReason => IsConfigured
        ? null
        : "No AI provider key is configured. Set the OPENROUTER_API_KEY environment variable (or Ai:ApiKey) and restart the application.";

    public async Task<AiChatResult> CompleteAsync(
        IReadOnlyList<AiChatMessageDto> messages,
        string? model = null,
        double? temperature = null,
        int? maxTokens = null,
        string? endUserId = null,
        CancellationToken cancellationToken = default)
    {
        var apiKey = ResolveApiKey();
        if (apiKey is null)
        {
            return AiChatResult.Failure(UnavailableReason!, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (messages is null || messages.Count == 0)
        {
            return AiChatResult.Failure("At least one message is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var cleaned = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new ChatMessage(NormalizeRole(message.Role), message.Content.Trim()))
            .ToList();

        if (cleaned.Count == 0)
        {
            return AiChatResult.Failure("At least one message must have content.", statusCode: StatusCodes.Status400BadRequest);
        }

        var totalCharacters = cleaned.Sum(message => message.Content.Length);
        if (totalCharacters > _options.MaxRequestCharacters)
        {
            return AiChatResult.Failure(
                $"The prompt is {totalCharacters} characters long, the maximum is {_options.MaxRequestCharacters}.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var payload = new ChatCompletionRequest
        {
            Model = string.IsNullOrWhiteSpace(model) ? _options.Model : model.Trim(),
            Messages = cleaned,
            Temperature = temperature,
            MaxTokens = maxTokens ?? (_options.DefaultMaxTokens > 0 ? _options.DefaultMaxTokens : null),
            User = string.IsNullOrWhiteSpace(endUserId) ? null : endUserId
        };

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, WriteOptions),
                Encoding.UTF8,
                "application/json")
        };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        // Both ranking headers are optional and only sent when configured
        if (!string.IsNullOrWhiteSpace(_options.SiteUrl))
        {
            request.Headers.TryAddWithoutValidation("HTTP-Referer", _options.SiteUrl.Trim());
        }

        if (!string.IsNullOrWhiteSpace(_options.SiteName))
        {
            request.Headers.TryAddWithoutValidation("X-OpenRouter-Title", _options.SiteName.Trim());
        }

        HttpResponseMessage response;

        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("The AI provider did not answer within {TimeoutSeconds} seconds", _options.TimeoutSeconds);
            return AiChatResult.Failure(
                $"The AI provider did not answer within {_options.TimeoutSeconds} seconds.",
                statusCode: StatusCodes.Status504GatewayTimeout);
        }
        catch (HttpRequestException exception)
        {
            _logger.LogWarning(exception, "The AI provider could not be reached");
            return AiChatResult.Failure(
                $"The AI provider could not be reached ({exception.Message}).",
                statusCode: StatusCodes.Status502BadGateway);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var (message, providerCode) = ReadProviderError(body, response.StatusCode);
                var detail = ReadProviderDetail(body);
                var retryAfter = ReadRetryAfter(response);

                _logger.LogWarning(
                    "The AI provider answered with {StatusCode} ({ProviderCode}) {Message} detail={ProviderDetail} retryAfter={RetryAfterSeconds}",
                    (int)response.StatusCode, providerCode, message, detail ?? "(none)", retryAfter);

                return AiChatResult.Failure(
                    message, providerCode, (int)response.StatusCode, detail, retryAfter);
            }

            return ReadCompletion(body, payload.Model);
        }
    }

    private AiChatResult ReadCompletion(string body, string requestedModel)
    {
        ChatCompletionResponse? parsed;

        try
        {
            parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, ReadOptions);
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "The AI provider returned a body that is not valid JSON");
            return AiChatResult.Failure(
                "The AI provider returned a response that could not be read.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        if (parsed is null)
        {
            return AiChatResult.Failure(
                "The AI provider returned an empty response.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        // A few providers report a problem with a 200 status and an error object
        if (parsed.Error is { } inlineError && !string.IsNullOrWhiteSpace(inlineError.Message))
        {
            return AiChatResult.Failure(inlineError.Message, inlineError.Code, StatusCodes.Status200OK);
        }

        var choice = parsed.Choices?.FirstOrDefault();

        if (choice?.Message is null)
        {
            return AiChatResult.Failure(
                "The AI provider returned no answer.",
                statusCode: StatusCodes.Status502BadGateway);
        }

        var usage = parsed.Usage;

        var response = new AiChatResponseDto(
            Content: choice.Message.Content ?? string.Empty,
            Model: string.IsNullOrWhiteSpace(parsed.Model) ? requestedModel : parsed.Model,
            FinishReason: choice.FinishReason,
            CompletionId: parsed.Id,
            PromptTokens: usage?.PromptTokens ?? 0,
            CompletionTokens: usage?.CompletionTokens ?? 0,
            TotalTokens: usage?.TotalTokens ?? 0,
            Truncated: string.Equals(choice.FinishReason, "length", StringComparison.OrdinalIgnoreCase));

        _logger.LogInformation(
            "AI completion used {PromptTokens} prompt and {CompletionTokens} generated tokens ({FinishReason})",
            response.PromptTokens, response.CompletionTokens, response.FinishReason ?? "unknown");

        return AiChatResult.Success(response);
    }

    // Pulls the provider message out of a failed response, falling back to a
    // status specific sentence when the body is not the documented error shape.
    private static (string Message, int? Code) ReadProviderError(string body, HttpStatusCode statusCode)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, ReadOptions);

            if (parsed?.Error is { } error && !string.IsNullOrWhiteSpace(error.Message))
            {
                return (error.Message, error.Code);
            }
        }
        catch (JsonException)
        {
            // The provider did not answer with the documented error shape
        }

        var summary = DescribeStatus(statusCode);
        var detail = Truncate(body, 300);

        return string.IsNullOrWhiteSpace(detail)
            ? (summary, null)
            : ($"{summary} {detail}", null);
    }

    // A generic "Provider returned error" says nothing, the real reason lives in
    // error.metadata: which upstream provider refused, its own status code and
    // whether a platform limit was the cause.
    private static string? ReadProviderDetail(string body)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<ChatCompletionResponse>(body, ReadOptions);
            var metadata = parsed?.Error?.Metadata;

            if (metadata is null)
            {
                return null;
            }

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(metadata.ProviderName))
            {
                parts.Add($"provider={metadata.ProviderName}");
            }

            if (metadata.ProviderCode is { } providerCode)
            {
                parts.Add($"providerCode={providerCode}");
            }

            if (!string.IsNullOrWhiteSpace(metadata.ErrorType))
            {
                parts.Add($"errorType={metadata.ErrorType}");
            }

            if (!string.IsNullOrWhiteSpace(metadata.LimitSource))
            {
                parts.Add($"limitSource={metadata.LimitSource}");
            }

            if (!string.IsNullOrWhiteSpace(metadata.Raw))
            {
                parts.Add($"raw={Truncate(metadata.Raw, 300)}");
            }

            return parts.Count > 0 ? string.Join(", ", parts) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // OpenRouter sends Retry-After when every provider it tried asked for a wait
    private static int? ReadRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;

        if (retryAfter is null)
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return Math.Max(1, (int)Math.Ceiling(delta.TotalSeconds));
        }

        return retryAfter.Date is { } date
            ? Math.Max(1, (int)Math.Ceiling((date - DateTimeOffset.UtcNow).TotalSeconds))
            : null;
    }

    private static string DescribeStatus(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.BadRequest => "The AI provider rejected the request as invalid.",
        HttpStatusCode.Unauthorized => "The AI provider rejected the configured API key.",
        HttpStatusCode.PaymentRequired => "The AI provider refused the request, the account has no credit left.",
        HttpStatusCode.TooManyRequests => "The AI provider is rate limiting this account, try again later.",
        HttpStatusCode.NotFound => "The AI provider does not know the configured model or endpoint.",
        _ => $"The AI provider answered with status {(int)statusCode}."
    };

    private static string NormalizeRole(string? role) => role?.Trim().ToLowerInvariant() switch
    {
        "system" => "system",
        "assistant" => "assistant",
        "tool" => "tool",
        _ => "user"
    };

    private static string Truncate(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var collapsed = value.Trim();

        return collapsed.Length <= maximumLength ? collapsed : collapsed[..maximumLength];
    }

    private string? ResolveApiKey()
    {
        var fromConfiguration = _options.ApiKey;
        if (!string.IsNullOrWhiteSpace(fromConfiguration))
        {
            return fromConfiguration.Trim();
        }

        var fromEnvironment = Environment.GetEnvironmentVariable("OPENROUTER_API_KEY");

        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
    }

    // ---- wire format -------------------------------------------------------

    private sealed class ChatCompletionRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public List<ChatMessage> Messages { get; init; } = [];

        [JsonPropertyName("temperature")]
        public double? Temperature { get; init; }

        [JsonPropertyName("max_tokens")]
        public int? MaxTokens { get; init; }

        // Lets the provider rate limit per end user instead of per account
        [JsonPropertyName("user")]
        public string? User { get; init; }
    }

    private sealed record ChatMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("model")]
        public string? Model { get; set; }

        [JsonPropertyName("choices")]
        public List<ChatChoice>? Choices { get; set; }

        [JsonPropertyName("usage")]
        public ChatUsage? Usage { get; set; }

        [JsonPropertyName("error")]
        public ProviderError? Error { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string? FinishReason { get; set; }
    }

    private sealed class ChatUsage
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("total_tokens")]
        public int TotalTokens { get; set; }
    }

    private sealed class ProviderError
    {
        [JsonPropertyName("code")]
        public int? Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        // Where the real reason hides: which upstream provider refused, its own
        // status code, and whether a platform limit was the cause
        [JsonPropertyName("metadata")]
        public ProviderErrorMetadata? Metadata { get; set; }
    }

    private sealed class ProviderErrorMetadata
    {
        [JsonPropertyName("provider_name")]
        public string? ProviderName { get; set; }

        [JsonPropertyName("provider_code")]
        public int? ProviderCode { get; set; }

        [JsonPropertyName("error_type")]
        public string? ErrorType { get; set; }

        [JsonPropertyName("limit_source")]
        public string? LimitSource { get; set; }

        [JsonPropertyName("raw")]
        public string? Raw { get; set; }
    }
}
