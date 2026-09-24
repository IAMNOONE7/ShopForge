using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Customers.Accounts;
using ShopForge.Customers.Authentication;
using ShopForge.Customers.Domain;
using ShopForge.Customers.Wishlist;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Security;

namespace ShopForge.Customers;

public static class CustomersModule
{
    internal const string Schema = "customers";

    public static Assembly Assembly => typeof(CustomersModule).Assembly;

    public static IServiceCollection AddCustomersModule(this IServiceCollection services, bool requireSecureCookies)
    {
        services.AddHttpContextAccessor();
        services.AddSingleton<IPasswordHasher<StoreCustomer>, PasswordHasher<StoreCustomer>>();
        services.AddScoped<ICurrentCustomer, CurrentCustomer>();
        services.AddScoped<CustomerMail>();
        services.AddScoped<IStoreMaintenance, RegistrationCleanup>();

        services.AddAuthentication().AddCookie(CustomerPolicies.Scheme, options =>
        {
            options.Cookie.Name = "shopforge_customer";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = requireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromDays(14);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = CustomerSessions.ValidateAsync;
            options.Events.OnRedirectToLogin = context => SetStatusCode(context.Response, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => SetStatusCode(context.Response, StatusCodes.Status403Forbidden);
        });

        services.AddAuthorizationBuilder()
            .AddPolicy(CustomerPolicies.Customer, policy => policy
                .AddAuthenticationSchemes(CustomerPolicies.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim(ShopForgeClaimTypes.StoreCustomerId));

        return services;
    }

    public static IEndpointRouteBuilder MapCustomersStorefrontEndpoints(this IEndpointRouteBuilder storefront)
    {
        storefront.MapCustomerAccounts();
        storefront.MapWishlist();

        return storefront;
    }

    private static Task SetStatusCode(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
