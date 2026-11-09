using System.Net.Http.Json;

namespace LedgerPay.Api.Tests;

public static class IdempotentRequests
{
    public static Task<HttpResponseMessage> PostWithKeyAsync<T>(
        this HttpClient client, string url, T body, CancellationToken ct, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return client.SendAsync(request, ct);
    }
}
