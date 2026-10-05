using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ShopForge.IntegrationTests.Orders;

// A storefront visitor: one HttpClient keeps the cart cookie across requests, always against the same store host.
internal sealed class StorefrontApi(WebApplicationFactory<Program> factory, TestStore store, bool followRedirects = true) : IDisposable
{
    // A client that follows a redirect cannot tell you there was one, which is the whole subject of some tests.
    private readonly HttpClient _client = factory.CreateClient(
        new WebApplicationFactoryClientOptions { AllowAutoRedirect = followRedirects });

    public async Task<HttpResponseMessage> GetAsync(string path, (string Name, string Value)? header = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Url(path));

        if (header is { } extra)
        {
            request.Headers.Add(extra.Name, extra.Value);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public async Task<HttpResponseMessage> PostAsync(string path, object? body, (string Name, string Value)? header = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Url(path))
        {
            Content = JsonContent.Create(body ?? new { }),
        };

        if (header is { } extra)
        {
            request.Headers.Add(extra.Name, extra.Value);
        }

        return await _client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    public Task<HttpResponseMessage> PutAsync(string path, object body) =>
        _client.PutAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string path) =>
        _client.DeleteAsync(Url(path), TestContext.Current.CancellationToken);

    public async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    // The answer as the browser would receive it, for the rare assertion about what is not in it.
    public async Task<string> GetStringAsync(string path)
    {
        using var response = await GetAsync(path);

        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    public async Task<T> GetJsonAsync<T>(string path)
    {
        using var response = await GetAsync(path);

        return await ReadAsync<T>(response);
    }

    public void Dispose() => _client.Dispose();

    private string Url(string path) => $"http://{store.HostName}{path}";
}

internal sealed record CartView(
    List<CartLineView> Items,
    int Count,
    decimal ItemsTotal,
    decimal VatTotal,
    bool Changed,
    DiscountView? Discount);

internal sealed record DiscountView(string Code, string Name, decimal Amount);

internal sealed record CartLineView(Guid StoreProductId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal, int Available);

internal sealed record PlacedOrder(string Number, Guid Token, string PaymentInstructions);

internal sealed record OrderView(
    string Number,
    string Status,
    string Email,
    string Currency,
    string PaymentMethod,
    string ShippingMethod,
    decimal ShippingPrice,
    decimal ItemsTotal,
    decimal VatTotal,
    decimal GrandTotal,
    DiscountView? Discount,
    string? PickupPoint,
    ShipmentView? Shipment,
    List<DocumentView> Documents,
    List<OrderLineView> Lines);

internal sealed record DocumentView(string Number, string Kind, DateTimeOffset IssuedAt);

internal sealed record ShipmentView(string Carrier, string TrackingNumber, string? TrackingUrl);

internal sealed record OrderLineView(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);

internal static class Checkout
{
    public static object Request(
        string? email = "buyer@example.test",
        string? payment = "bank-transfer",
        string? shipping = "courier",
        string? pickupPoint = null,
        string? phone = "+420 123 456 789") => new
        {
            Email = email,
            Phone = phone,
            BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Dublin", PostalCode = "D01 AB12", Country = "IE" },
            ShippingAddress = (object?)null,
            PaymentMethodCode = payment,
            ShippingMethodCode = shipping,
            PickupPointCode = pickupPoint,
        };
}
