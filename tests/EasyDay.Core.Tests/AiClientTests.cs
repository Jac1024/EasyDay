using System.Net;
using System.Text;
using EasyDay.Core.Ai;
using EasyDay.Core.Models;

namespace EasyDay.Core.Tests;

public class AiClientTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public string Response { get; set; } =
            """{"choices":[{"message":{"content":"{\"events\":[]}"}}]}""";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(Status)
            {
                Content = new StringContent(Response, Encoding.UTF8, "application/json")
            };
        }
    }

    private static AppSettings Settings() => new()
    {
        ApiBaseUrl = "https://api.example.com/v1",
        ApiKey = "test-key",
        Model = "demo-model"
    };

    [Fact]
    public async Task Posts_to_chat_completions()
    {
        var handler = new CapturingHandler();
        var settings = Settings();
        var client = new ChatCompletionsClient(() => settings, new HttpClient(handler));

        await client.CompleteJsonAsync(new AiRequest("system", "user"));

        Assert.NotNull(handler.Request);
        Assert.Equal("https://api.example.com/v1/chat/completions", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization?.Scheme);
    }

    [Fact]
    public async Task Applies_extra_headers()
    {
        var handler = new CapturingHandler();
        var settings = Settings();
        settings.ApiExtraHeaders = "X-Tenant: easyday\nX-Trace: 123";
        var client = new ChatCompletionsClient(() => settings, new HttpClient(handler));

        await client.CompleteJsonAsync(new AiRequest("system", "user"));

        Assert.Equal("easyday", handler.Request!.Headers.GetValues("X-Tenant").Single());
        Assert.Equal("123", handler.Request.Headers.GetValues("X-Trace").Single());
    }

    [Fact]
    public async Task Forwards_server_error_payload()
    {
        var handler = new CapturingHandler
        {
            Status = HttpStatusCode.BadRequest,
            Response = """{"error":{"message":"bad request"}}"""
        };
        var client = new ChatCompletionsClient(() => Settings(), new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.CompleteJsonAsync(new AiRequest("system", "user")));

        Assert.Contains("400", ex.Message);
        Assert.Contains("bad request", ex.Message);
    }

    [Fact]
    public async Task Rejects_incomplete_configuration()
    {
        var settings = Settings();
        settings.Model = string.Empty;
        var client = new ChatCompletionsClient(() => settings, new HttpClient(new CapturingHandler()));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.CompleteJsonAsync(new AiRequest("system", "user")));

        Assert.Contains("未配置完整", ex.Message);
    }
}
