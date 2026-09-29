using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Platform.Authentication;
using ShopForge.Platform.Domain;
using ShopForge.Platform.Users;
using ShopForge.Shared.Maintenance;
using ShopForge.Shared.Security;

namespace ShopForge.Platform;

public static class PlatformModule
{
    internal const string Schema = "platform";

    public static Assembly Assembly => typeof(PlatformModule).Assembly;

    public static IServiceCollection AddPlatformModule(this IServiceCollection services, bool requireSecureCookies)
    {
        services.AddSingleton<IPasswordHasher<PlatformUser>, PasswordHasher<PlatformUser>>();
        services.AddScoped<PlatformMail>();
        services.AddScoped<IMaintenanceOutsideStores, PlatformCleanup>();

        services.AddAuthentication().AddCookie(PlatformPolicies.Scheme, options =>
        {
            options.Cookie.Name = "shopforge_platform";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = requireSecureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = PlatformSessions.ValidateAsync;
            options.Events.OnRedirectToLogin = context => SetStatusCode(context.Response, StatusCodes.Status401Unauthorized);
            options.Events.OnRedirectToAccessDenied = context => SetStatusCode(context.Response, StatusCodes.Status403Forbidden);
        });

        // The scheme is named on the policy, so an admin or customer cookie on the same request proves nothing here.
        services.AddAuthorizationBuilder()
            .AddPolicy(PlatformPolicies.PlatformUser, policy => policy
                .AddAuthenticationSchemes(PlatformPolicies.Scheme)
                .RequireAuthenticatedUser());

        return services;
    }

    public static IEndpointRouteBuilder MapPlatformAuthEndpoints(this IEndpointRouteBuilder platform)
    {
        platform.MapPlatformAuth();
        platform.MapPlatformOpenEndpoints();

        return platform;
    }

    public static IEndpointRouteBuilder MapPlatformOperatorEndpoints(this IEndpointRouteBuilder platformOperator)
    {
        platformOperator.MapOperatorEndpoints();
        platformOperator.MapGroup("/account").MapPlatformTwoFactorEndpoints();

        return platformOperator;
    }

    private static Task SetStatusCode(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
