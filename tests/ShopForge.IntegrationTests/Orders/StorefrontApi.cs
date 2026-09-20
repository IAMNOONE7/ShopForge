using System.Net;
using System.Net.Http.Json;

namespace ShopForge.IntegrationTests.Orders;

// A storefront visitor: one HttpClient keeps the cart cookie across requests, always against the same store host.
internal sealed class StorefrontApi(ShopForgeApiFactory factory, TestStore store) : IDisposable
{
    private readonly HttpClient _client = factory.CreateClient();

    public Task<HttpResponseMessage> GetAsync(string path) =>
        _client.GetAsync(Url(path), TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> PostAsync(string path, object? body) =>
        _client.PostAsJsonAsync(Url(path), body ?? new { }, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> PutAsync(string path, object body) =>
        _client.PutAsJsonAsync(Url(path), body, TestContext.Current.CancellationToken);

    public Task<HttpResponseMessage> DeleteAsync(string path) =>
        _client.DeleteAsync(Url(path), TestContext.Current.CancellationToken);

    public async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.Equal(expected, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<T>(TestContext.Current.CancellationToken))!;
    }

    public async Task<T> GetJsonAsync<T>(string path)
    {
        using var response = await GetAsync(path);

        return await ReadAsync<T>(response);
    }

    public void Dispose() => _client.Dispose();

    private string Url(string path) => $"http://{store.HostName}{path}";
}

internal sealed record CartView(List<CartLineView> Items, int Count, decimal ItemsTotal, decimal VatTotal, int RemovedLines);

internal sealed record CartLineView(Guid StoreProductId, string Name, decimal UnitPrice, int Quantity, decimal LineTotal);

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
    List<OrderLineView> Lines);

internal sealed record OrderLineView(string ProductName, decimal UnitPrice, decimal VatRate, int Quantity, decimal LineTotal);

internal static class Checkout
{
    public static object Request(string? email = "buyer@example.test", string? payment = "bank-transfer", string? shipping = "courier") => new
    {
        Email = email,
        BillingAddress = new { FullName = "Alex Buyer", Line1 = "1 Main Street", Line2 = (string?)null, City = "Dublin", PostalCode = "D01 AB12", Country = "IE" },
        ShippingAddress = (object?)null,
        PaymentMethodCode = payment,
        ShippingMethodCode = shipping,
    };
}
