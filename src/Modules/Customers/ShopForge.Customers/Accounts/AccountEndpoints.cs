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
using ShopForge.Customers.Privacy;
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
        account.MapPost("/verification/resend", ResendVerificationAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/login", LoginAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/logout", Logout);
        account.MapPost("/password/forgot", ForgotPasswordAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/password/reset", ResetPasswordAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapGet("/me", GetProfileAsync).RequireAuthorization(CustomerPolicies.Customer);
        account.MapPut("/me", UpdateProfileAsync).RequireAuthorization(CustomerPolicies.Customer);
        account.MapPost("/email", ChangeEmailAsync)
            .RequireAuthorization(CustomerPolicies.Customer)
            .RequireRateLimiting(RateLimits.Authentication);
        account.MapPost("/email/confirm", ConfirmEmailAsync).RequireRateLimiting(RateLimits.Authentication);
        account.MapPrivacyEndpoints();
        account.MapConsentEndpoints();
    }

    // The answer never says whether the address is already known (D-052); only the e-mail that follows differs.
    private static async Task<Results<Accepted, ValidationProblem>> RegisterAsync(
        RegisterRequest request,
        DbContext dbContext,
        IStoreContext storeContext,
        IPasswordHasher<StoreCustomer> passwordHasher,
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

        // Hashing is what makes a registration slow, so it happens on every path: an address that already has an
        // account here must not be given away by a faster answer (D-052).
        var passwordHash = passwordHasher.HashPassword(null!, request.Password!);
        var identity = await dbContext.Set<CustomerIdentity>().SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);

        // Only this store's relationship counts. An address the company knows from another store is registered here
        // from scratch, with its own password, and nothing of the other store's account is read or changed (D-102).
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
            dbContext.Add(identity);
        }

        var now = clock.GetUtcNow();
        var firstName = request.FirstName!.Trim();
        var lastName = request.LastName!.Trim();
        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        var pending = await dbContext.Set<PendingRegistration>()
            .SingleOrDefaultAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken);

        if (pending is null)
        {
            dbContext.Add(new PendingRegistration(identity.Id, firstName, lastName, phone, passwordHash, storeContext.StoreId!.Value, now));
        }
        else
        {
            pending.Replace(firstName, lastName, phone, passwordHash, now);
        }

        await mail.SendVerificationAsync(identity, now, cancellationToken);

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

        // The token is owned by the store that sent it, so a link from another store of the company is not found
        // here at all (D-102).
        var token = await UsableTokenAsync(dbContext, request.Token, CustomerTokenPurpose.EmailVerification, now, cancellationToken);

        if (token is null)
        {
            return InvalidToken();
        }

        var identity = await dbContext.Set<CustomerIdentity>().SingleAsync(candidate => candidate.Id == token.CustomerIdentityId, cancellationToken);
        var pending = await dbContext.Set<PendingRegistration>()
            .SingleOrDefaultAsync(registration => registration.CustomerIdentityId == identity.Id, cancellationToken);
        var customer = await dbContext.Set<StoreCustomer>()
            .SingleOrDefaultAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken);

        if (customer is null)
        {
            if (pending is null)
            {
                return InvalidToken();
            }

            // The password waiting in the registration becomes this store's credential, and only now: until the
            // address is proved there is no account here to sign in to.
            customer = new StoreCustomer(
                storeContext.StoreId!.Value,
                identity.Id,
                pending.FirstName,
                pending.LastName,
                pending.Phone,
                pending.PasswordHash);
            dbContext.Add(customer);
        }

        if (pending is not null)
        {
            dbContext.Remove(pending);
        }

        customer.VerifyEmail();
        token.Use(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await orders.ClaimAsync(customer.Id, identity.Email, cancellationToken);

        await httpContext.SignInAsync(CustomerPolicies.Scheme, CustomerSessions.PrincipalFor(identity, customer));

        return TypedResults.Ok(CustomerResponse.From(identity, customer));
    }

    // A link that expired or never arrived is asked for again here, rather than by registering a second time.
    // As everywhere else, the answer says nothing about which addresses this store knows (D-052).
    private static async Task<Accepted> ResendVerificationAsync(
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
            // Only this store's sign-up is resent; a registration waiting at another store of the company is not
            // this store's business (D-102).
            var waiting = await dbContext.Set<PendingRegistration>()
                .AnyAsync(registration => registration.CustomerIdentityId == identity.Id, cancellationToken);

            if (waiting)
            {
                await mail.SendVerificationAsync(identity, clock.GetUtcNow(), cancellationToken);
            }
            else if (await dbContext.Set<StoreCustomer>().AnyAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken))
            {
                await mail.SendAccountExistsAsync(identity, cancellationToken);
            }
        }

        return TypedResults.Accepted((string?)null);
    }

    // Asking is not changing: the address moves only when the link sent to it is followed (D-115).
    private static async Task<Results<Accepted, ValidationProblem, ProblemHttpResult, UnauthorizedHttpResult>> ChangeEmailAsync(
        ChangeEmailRequest request,
        DbContext dbContext,
        ICurrentCustomer currentCustomer,
        IPasswordHasher<StoreCustomer> passwordHasher,
        CustomerMail mail,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var errors = new RequestErrors().Check(Emails.IsValid(request.NewEmail), "newEmail", "A valid e-mail address is required.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var account = await currentCustomer.FindAsync(cancellationToken);
        var customer = account is null
            ? null
            : await dbContext.Set<StoreCustomer>().SingleOrDefaultAsync(candidate => candidate.Id == account.StoreCustomerId, cancellationToken);

        if (account is null || customer is null)
        {
            return TypedResults.Unauthorized();
        }

        // An open session is not enough to move where the account's mail goes.
        if (passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.Password ?? "") == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "That is not your password");
        }

        var newEmail = CustomerIdentity.NormalizeEmail(request.NewEmail!);

        if (newEmail == account.Email)
        {
            return TypedResults.Accepted((string?)null);
        }

        var taken = await dbContext.Set<CustomerIdentity>().SingleOrDefaultAsync(candidate => candidate.Email == newEmail, cancellationToken);

        // An address with an account at this store is told so at the address itself, exactly as registering with it
        // would be: the person asking learns nothing either way (D-052).
        if (taken is not null && await dbContext.Set<StoreCustomer>().AnyAsync(candidate => candidate.CustomerIdentityId == taken.Id, cancellationToken))
        {
            await mail.SendAccountExistsAsync(taken, cancellationToken);

            return TypedResults.Accepted((string?)null);
        }

        await mail.SendEmailChangeAsync(customer, newEmail, clock.GetUtcNow(), cancellationToken);

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<Ok<CustomerResponse>, ProblemHttpResult>> ConfirmEmailAsync(
        TokenRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IStoreContext storeContext,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var hash = string.IsNullOrWhiteSpace(request.Token) ? null : TokenValues.Hash(request.Token);

        // The store filter is what keeps a link from one shop from moving an account at another (D-102).
        var change = hash is null
            ? null
            : await dbContext.Set<EmailChange>().SingleOrDefaultAsync(candidate => candidate.TokenHash == hash, cancellationToken);

        if (change?.IsUsable(now) != true)
        {
            return InvalidToken();
        }

        var customer = await dbContext.Set<StoreCustomer>().SingleOrDefaultAsync(candidate => candidate.Id == change.StoreCustomerId, cancellationToken);

        if (customer is null)
        {
            return InvalidToken();
        }

        var identity = await dbContext.Set<CustomerIdentity>().SingleOrDefaultAsync(candidate => candidate.Email == change.NewEmail, cancellationToken);

        // Somebody may have taken the address here between the asking and the confirming.
        if (identity is not null && await dbContext.Set<StoreCustomer>().AnyAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "That address already has an account here");
        }

        if (identity is null)
        {
            identity = new CustomerIdentity(storeContext.TenantId!.Value, change.NewEmail);
            dbContext.Add(identity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        customer.MoveTo(identity.Id);
        change.Use(now);
        await dbContext.SaveChangesAsync(cancellationToken);

        // Sessions carry the identity they were issued for, so every other one is already out; this browser proved
        // the new address, so it is signed in again with it.
        await httpContext.SignInAsync(CustomerPolicies.Scheme, CustomerSessions.PrincipalFor(identity, customer));

        return TypedResults.Ok(CustomerResponse.From(identity, customer));
    }

    private static async Task<Results<Ok<CustomerResponse>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<StoreCustomer> passwordHasher,
        CancellationToken cancellationToken)
    {
        var email = CustomerIdentity.NormalizeEmail(request.Email ?? "");
        var identity = await dbContext.Set<CustomerIdentity>().SingleOrDefaultAsync(candidate => candidate.Email == email, cancellationToken);

        // The password checked is the one held by this store's relationship: the store filter means an account of
        // another store of the company is not even a candidate (D-102).
        var customer = identity is null
            ? null
            : await dbContext.Set<StoreCustomer>().SingleOrDefaultAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken);
        var verification = customer is null
            ? Passwords.VerifyAgainstDummyHash(passwordHasher, request.Password)
            : passwordHasher.VerifyHashedPassword(customer, customer.PasswordHash, request.Password ?? "");

        if (identity is null || customer is null || !customer.IsEmailVerified || verification == PasswordVerificationResult.Failed)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Invalid e-mail or password");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            customer.SetPasswordHash(passwordHasher.HashPassword(customer, request.Password!));
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

        // Only a customer of this store gets a link. An address the company knows from another store is a stranger
        // here, and a shop a customer never registered with does not write to them (D-102).
        var known = identity is not null
            && await dbContext.Set<StoreCustomer>().AnyAsync(candidate => candidate.CustomerIdentityId == identity.Id, cancellationToken);

        if (known)
        {
            await mail.SendPasswordResetAsync(identity!, clock.GetUtcNow(), cancellationToken);
        }

        return TypedResults.Accepted((string?)null);
    }

    private static async Task<Results<NoContent, ValidationProblem, ProblemHttpResult>> ResetPasswordAsync(
        ResetPasswordRequest request,
        HttpContext httpContext,
        DbContext dbContext,
        IPasswordHasher<StoreCustomer> passwordHasher,
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

        // The token belongs to this store, so the password changed is this store's; another store's account keeps
        // the password it has (D-102).
        var customer = await dbContext.Set<StoreCustomer>()
            .SingleOrDefaultAsync(candidate => candidate.CustomerIdentityId == token.CustomerIdentityId, cancellationToken);

        if (customer is null)
        {
            return InvalidToken();
        }

        customer.SetPasswordHash(passwordHasher.HashPassword(customer, request.Password!));
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

internal sealed record ChangeEmailRequest(string? NewEmail, string? Password);

internal sealed record CustomerResponse(string Email, string FirstName, string LastName, string? Phone)
{
    public static CustomerResponse From(CustomerIdentity identity, StoreCustomer customer) =>
        new(identity.Email, customer.FirstName, customer.LastName, customer.Phone);
}
