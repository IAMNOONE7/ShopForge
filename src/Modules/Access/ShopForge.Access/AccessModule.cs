using System.Reflection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShopForge.Access.Authentication;
using ShopForge.Access.Domain;
using ShopForge.Shared.Security;

namespace ShopForge.Access;

public static class AccessModule
{
    internal const string Schema = "access";

    public static Assembly Assembly => typeof(AccessModule).Assembly;

    public static IServiceCollection AddAccessModule(this IServiceCollection services)
    {
        services.AddSingleton<IPasswordHasher<TenantUser>, PasswordHasher<TenantUser>>();

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "shopforge_admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Events.OnRedirectToLogin = context => SetStatusCode(context.Response, StatusCodes.Status401Unauthorized);
                options.Events.OnRedirectToAccessDenied = context => SetStatusCode(context.Response, StatusCodes.Status403Forbidden);
            });

        services.AddAuthorizationBuilder()
            .AddPolicy(AdminPolicies.TenantUser, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ShopForgeClaimTypes.TenantId))
            .AddPolicy(AdminPolicies.CatalogManagement, policy => policy
                .RequireAuthenticatedUser()
                .RequireClaim(ShopForgeClaimTypes.TenantId)
                .RequireRole(nameof(TenantRole.Owner), nameof(TenantRole.Admin), nameof(TenantRole.CatalogManager)));

        return services;
    }

    public static IEndpointRouteBuilder MapAccessAdminEndpoints(this IEndpointRouteBuilder admin)
    {
        admin.MapGroup("/auth").MapAuthEndpoints();

        return admin;
    }

    private static Task SetStatusCode(HttpResponse response, int statusCode)
    {
        response.StatusCode = statusCode;
        return Task.CompletedTask;
    }
}
