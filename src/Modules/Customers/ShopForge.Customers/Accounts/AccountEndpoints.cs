using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Customers.Authentication;
using ShopForge.Customers.Domain;
using ShopForge.Shared.Customers;
using ShopForge.Shared.Email;
using ShopForge.Shared.Http;
using ShopForge.Shared.Security;
using ShopForge.Shared.Tenancy;

namespace ShopForge.Customers.Accounts;

internal static class AccountEndpoints
{
    private const int MinPasswordLength = 10;
    private const int MaxPasswordLength = 128;

    public static void MapCustomerAccounts(this IEndpointRouteBuilder storefront)
    {
        var account = storefront.MapGroup("/account");

        account.MapPost("/register", RegisterAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/verify", VerifyAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/logout", Logout);
        account.MapPost("/password/forgot", ForgotPasswordAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/password/reset", ResetPasswordAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapGet("/me", GetProfileAsync).RequireAuthorization(CustomerPolicies.Customer);
        account.MapPut("/me", UpdateProfileAsync).RequireAuthorization(CustomerPolicies.Customer);
    }

    // The answer never says whether the address is already known (D-052); only the e-mail that follows differs.
    private static async Task<Results<Accepted, ValidationProblem>> RegisterAsync(
        RegisterRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IPasswordHasher<CustomerIdentity> passwordHasher,
        CustomerMail mail,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(Emails.IsValid(request.Email), "email", "A valid e-mail address is required.")
            .Check(IsAcceptablePassword(request.Password), "password", $"The password needs at least {MinPasswordLength} characters.")
            .Check(IsName(request.FirstName), "firstName", "First name is required.")
            .Check(IsName(request.LastName), "lastName", "Last name is required.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var email = CustomerIdentity.NormalizeEmail(request.Email!);
        var identity = await dbContext.Set<CustomerIdentity>().SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        var customer = identity is null
            ? null
            : await dbContext.Set<StoreCustomer>().SingleOrDefaultAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken);

        if (identity is not null && customer is not null)
        {
            await mail.SendAccountExistsAsync(identity, cancellationToken);

            return TypedResults.Accepted((string?)null);
        }

        if (identity is null)
        {
            identity = new CustomerIdentity(storeContext.TenantId!.Value, email);
            identity.SetPasswordHash(passwordHasher.HashPassword(identity, request.Password!));
            dbContext.Add(identity);
        }

        dbContext.Add(new PendingRegistration(
            identity.Id,
            request.FirstName!.Trim(),
            request.LastName!.Trim(),
            string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            storeContext.StoreId!.Value,
            clock.GetUtcNow()));

        await mail.SendVerificationAsync(identity, clock.GetUtcNow(), cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    // Proving the address creates the store relationship, signs the customer in and hands them their guest orders.
    private static async Task<Results<Ok<CustomerResponse>, ProblemHttpResult>> VerifyAsync(
        TokenRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        ICustomerOrders orders,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var token = await UsableTokenAsync(dbContext, request.Token, CustomerTokenPurpose.EmailVerification, now, cancellationToken);

        if (token is null || token.StoreId != storeContext.StoreId)
        {
            return InvalidToken();
        }

        var identity = await dbContext.Set<CustomerIdentity>().SingleAsync(candidate => candidate.Id == token.CustomerIdentityId, cancellationToken);
        var pending = await dbContext.Set<PendingRegistration>()
            .Where(registration => registration.CustomerIdentityId == identity.Id && registration.StoreId == storeContext.StoreId)
            .SingleOrDefaultAsync(cancellationToken);
        var customer = await dbContext.Set<StoreCustomer>()
            .SingleOrDefaultAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken);

        if (customer is null)
        {
            if (pending is null)
            {
                return InvalidToken();
            }

            customer = new StoreCustomer(storeContext.StoreId!.Value, identity.Id, pending.FirstName, pending.LastName, pending.Phone);
            dbContext.Add(customer);
        }

        if (pending is not null)
        {
            dbContext.Remove(pending);
        }

        identity.VerifyEmail();
        token.Use(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await orders.ClaimAsync(customer.Id, identity.Email, cancellationToken);

        await httpContext.SignInAsync(CustomerPolicies.Scheme, CustomerSessions.PrincipalFor(identity, customer));

        return TypedResults.Ok(CustomerResponse.From(identity, customer));
    }

    private static async Task<Results<Ok<CustomerResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<CustomerIdentity> passwordHasher,
        CancellationToken cancellationToken)
    {
        var email = CustomerIdentity.NormalizeEmail(request.Email ?? "");
        var identity = await dbContext.Set<CustomerIdentity>().SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);
        var verification = identity is null
            ? Passwords.VerifyAgainstDummyHash(passwordHasher, request.Password)
            : passwordHasher.VerifyHashedPassword(identity, identity.PasswordHash, request.Password ?? "");

        // A customer of another store of the same tenant looks exactly like a wrong password (D-050).
        var customer = identity is null || verification == PasswordVerificationResult.Failed
            ? null
            : await dbContext.Set<StoreCustomer>().SingleOrDefaultAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken);

        if (identity is null || customer is null || !identity.IsEmailVerified || verification == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid e-mail or password");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            identity.SetPasswordHash(passwordHasher.HashPassword(identity, request.Password!));
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await httpContext.SignInAsync(CustomerPolicies.Scheme, CustomerSessions.PrincipalFor(identity, customer));

        return TypedResults.Ok(CustomerResponse.From(identity, customer));
    }

    private static SignOutHttpResult Logout() => TypedResults.SignOut(authenticationSchemes: [CustomerPolicies.Scheme]);

    private static async Task<Accepted> ForgotPasswordAsync(
        EmailRequest request,
        DbContext dbContext,
        CustomerMail mail,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var email = CustomerIdentity.NormalizeEmail(request.Email ?? "");
        var identity = await dbContext.Set<CustomerIdentity>().SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);

        if (identity is not null)
        {
            await mail.SendPasswordResetAsync(identity, clock.GetUtcNow(), cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ResetPasswordAsync(
        ResetPasswordRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<CustomerIdentity> passwordHasher,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(IsAcceptablePassword(request.Password), "password", $"The password needs at least {MinPasswordLength} characters.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var now = clock.GetUtcNow();
        var token = await UsableTokenAsync(dbContext, request.Token, CustomerTokenPurpose.PasswordReset, now, cancellationToken);

        if (token is null)
        {
            return InvalidToken();
        }

        var identity = await dbContext.Set<CustomerIdentity>().SingleAsync(candidate => candidate.Id == token.CustomerIdentityId, cancellationToken);
        identity.SetPasswordHash(passwordHasher.HashPassword(identity, request.Password!));
        token.Use(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Whoever was signed in with the old password is signed out here.
        await httpContext.SignOutAsync(CustomerPolicies.Scheme);

        return TypedResults.NoContent();
    }

    private static async Task<Results<Ok<CustomerResponse>, UnauthorizedHttpResult>> GetProfileAsync(
        ClaimsPrincipal user,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        CancellationToken cancellationToken)
    {
        var customer = await FindAsync(dbContext, currentCustomer, cancellationToken);

        return customer is null
            ? TypedResults.Unauthorized()
            : TypedResults.Ok(new CustomerResponse(user.FindFirstValue(ClaimTypes.Email)!, customer.FirstName, customer.LastName, customer.Phone));
    }

    private static async Task<Results<Ok<CustomerResponse>, ValidationProblem, UnauthorizedHttpResult>> UpdateProfileAsync(
        ProfileRequest request,
        ClaimsPrincipal user,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors()
            .Check(IsName(request.FirstName), "firstName", "First name is required.")
            .Check(IsName(request.LastName), "lastName", "Last name is required.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var customer = await FindAsync(dbContext, currentCustomer, cancellationToken);

        if (customer is null)
        {
            return TypedResults.Unauthorized();
        }

        customer.SetDetails(request.FirstName!, request.LastName!, request.Phone);
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(new CustomerResponse(user.FindFirstValue(ClaimTypes.Email)!, customer.FirstName, customer.LastName, customer.Phone));
    }

    private static async Task<StoreCustomer?> FindAsync(DbContext dbContext, ICurrentCustomer currentCustomer, CancellationToken cancellationToken) =>
        await currentCustomer.FindAsync(cancellationToken) is { StoreCustomerId: var id }
            ? await dbContext.Set<StoreCustomer>().SingleOrDefaultAsync(customer => customer.Id == id, cancellationToken)
            : null;

    private static async Task<CustomerToken?> UsableTokenAsync(
        DbContext dbContext,
        string? value,
        CustomerTokenPurpose purpose,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hash = TokenValues.Hash(value);
        var token = await dbContext.Set<CustomerToken>()
            .SingleOrDefaultAsync(candidate => candidate.TokenHash == hash && candidate.Purpose == purpose, cancellationToken);

        return token?.IsUsable(now) == true ? token : null;
    }

    private static ProblemHttpResult InvalidToken() =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "The link is no longer valid",
            detail: "Ask for a new one and use the newest e-mail.");

    private static bool IsAcceptablePassword(string? password) =>
        password is not null && password.Length >= MinPasswordLength && password.Length <= MaxPasswordLength;

    private static bool IsName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= StoreCustomer.MaxNameLength;
}

internal sealed record RegisterRequest(string? Email, string? Password, string? FirstName, string? LastName, string? Phone);

internal sealed record LoginRequest(string? Email, string? Password);

internal sealed record EmailRequest(string? Email);

internal sealed record TokenRequest(string? Token);

internal sealed record ResetPasswordRequest(string? Token, string? Password);

internal sealed record ProfileRequest(string? FirstName, string? LastName, string? Phone);

internal sealed record CustomerResponse(string Email, string FirstName, string LastName, string? Phone)
{
    public static CustomerResponse From(CustomerIdentity identity, StoreCustomer customer) =>
        new(identity.Email, customer.FirstName, customer.LastName, customer.Phone);
}
