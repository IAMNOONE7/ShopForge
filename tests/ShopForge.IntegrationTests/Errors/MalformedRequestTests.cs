using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.IntegrationTests.Catalog;
using ShopForge.IntegrationTests.Orders;

namespace ShopForge.IntegrationTests.Errors;

public sealed class MalformedRequestTests(ShopForgeApiFactory factory)
{
    [Fact]
    public async Task A_body_the_api_cannot_read_is_the_caller_s_mistake()
    {
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var guest = new StorefrontApi(factory, furniture.Store);

        using var response = await guest.PostAsync("/api/storefront/cart/items", new { StoreProductId = (string?)null, Quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // Development lets the framework throw instead of answering; the answer has to be the same one.
    [Fact]
    public async Task It_stays_the_caller_s_mistake_where_the_framework_throws()
    {
        await using var throwing = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true)));
        var furniture = await FurnitureStore.CreateAsync(factory);
        using var guest = new StorefrontApi(throwing, furniture.Store);

        using var response = await guest.PostAsync("/api/storefront/cart/items", new { StoreProductId = (string?)null, Quantity = 1 });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
