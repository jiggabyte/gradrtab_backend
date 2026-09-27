using System.Net;
using System.Text;

namespace GradrTab.Tests.Support;

// Answers with a canned response instead of calling a real provider and records
// what was sent, so a test can assert on the method, url, headers and body.
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public StubHttpMessageHandler(HttpStatusCode statusCode, string body, string contentType = "application/json")
        : this(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, contentType)
        })
    {
    }

    public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responder = responder;
    }

    // The last request that reached the handler, null when nothing was sent
    public HttpRequestMessage? LastRequest { get; private set; }

    // The body of the last request, captured before the request is disposed
    public string? LastBody { get; private set; }

    public string? LastAuthorization =>
        LastRequest?.Headers.Authorization?.ToString();

    public string? LastHeader(string name) =>
        LastRequest?.Headers.TryGetValues(name, out var values) == true
            ? string.Join(",", values)
            : null;

    // Minimal IHttpClientFactory that hands out one client for the named client
    public static IHttpClientFactory Factory(HttpMessageHandler handler, string name = "openrouter")
    {
        return new SingleClientFactory(new HttpClient(handler), name);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        LastBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);

        return _responder(request);
    }

    private sealed class SingleClientFactory(HttpClient client, string name) : IHttpClientFactory
    {
        public HttpClient CreateClient(string clientName) =>
            string.Equals(clientName, name, StringComparison.OrdinalIgnoreCase) ? client : new HttpClient();
    }
}
