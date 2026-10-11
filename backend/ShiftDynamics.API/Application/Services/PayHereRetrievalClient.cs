using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ShiftDynamics.API.Common;

namespace ShiftDynamics.API.Application.Services;

public sealed class PayHereRetrievalClient : IDisposable
{
    private readonly IConfiguration _config;
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _token, _credentialKey;
    private DateTime _expires, _nextRequest;
    public PayHereRetrievalClient(IConfiguration config) => _config = config;
    public bool Configured => !string.IsNullOrWhiteSpace(_config["PayHere:AppId"]) &&
        !string.IsNullOrWhiteSpace(_config["PayHere:AppSecret"]);

    public async Task<IReadOnlyList<RetrievedPayHerePayment>> LookupAsync(string order, CancellationToken ct)
    {
        if (!Configured) throw new ConflictException("Configure PayHere Sandbox App ID and App Secret in .NET user secrets.");
        if (!PayHereSecurity.TryOrder(order, out _)) throw new ValidationException("Invalid Sandbox payment order.");
        await _gate.WaitAsync(ct);
        try
        {
            var credentials = _config["PayHere:AppId"] + ":" + _config["PayHere:AppSecret"];
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credentials)));
            if (_token is null || _expires <= DateTime.UtcNow || key != _credentialKey)
                await AuthorizeAsync(credentials, key, ct);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get,
                    "https://sandbox.payhere.lk/merchant/v1/payment/search?order_id=" + Uri.EscapeDataString(order));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                await ThrottleAsync(ct);
                using var response = await _http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
                { _token = null; await AuthorizeAsync(credentials, key, ct); continue; }
                if (!response.IsSuccessStatusCode) throw new ConflictException("PayHere lookup failed. Check Sandbox API credentials, permissions and allowed domain (*), then retry.");
                var json = await response.Content.ReadAsStringAsync(ct);
                if (json.Length > 1_000_000) throw new ConflictException("Unexpected PayHere response size.");
                try { return PayHereRetrievalParser.Parse(json, order); }
                catch (Exception e) when (e is JsonException or InvalidDataException or InvalidOperationException or FormatException)
                { throw new ConflictException("PayHere could not verify this order. Check Sandbox credentials and allowed domain (*). The invoice has not been changed."); }
            }
            throw new ConflictException("PayHere authentication failed. Check your Sandbox API credentials.");
        }
        catch (HttpRequestException) { throw new ConflictException("Unable to reach PayHere Sandbox. Check internet access and retry."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new ConflictException("PayHere lookup timed out. Retry to check payment status."); }
        finally { _gate.Release(); }
    }

    private async Task ThrottleAsync(CancellationToken ct)
    {
        var delay = _nextRequest - DateTime.UtcNow;
        if (delay > TimeSpan.Zero) await Task.Delay(delay, ct);
        _nextRequest = DateTime.UtcNow.AddMilliseconds(650);
    }
    private async Task AuthorizeAsync(string credentials, string key, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://sandbox.payhere.lk/merchant/v1/oauth/token");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(credentials)));
        request.Content = new FormUrlEncodedContent(new Dictionary<string,string> { ["grant_type"] = "client_credentials" });
        await ThrottleAsync(ct);
        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new ConflictException("Sandbox API authentication failed. Check App ID, App Secret, API permissions and allowed domain (*).");
        var json = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("access_token", out var t) || string.IsNullOrWhiteSpace(t.GetString()) ||
                !root.TryGetProperty("expires_in", out var expires) || !expires.TryGetInt32(out var seconds) || seconds <= 0 ||
                !root.TryGetProperty("scope", out var scope) || !(scope.GetString() ?? "").Split(' ').Contains("SANDBOX"))
                throw new ConflictException("PayHere did not issue a Sandbox API token.");
            _token = t.GetString(); _credentialKey = key;
            _expires = DateTime.UtcNow.AddSeconds(Math.Max(1, Math.Min(600, seconds) - 30));
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException)
        { throw new ConflictException("Unexpected PayHere authentication response."); }
    }
    public void Dispose() { _http.Dispose(); _gate.Dispose(); }
}
