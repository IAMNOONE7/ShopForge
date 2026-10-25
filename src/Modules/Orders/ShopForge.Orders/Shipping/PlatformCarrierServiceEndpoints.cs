using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using ShopForge.Orders.Domain;
using ShopForge.Shared.Auditing;
using ShopForge.Shared.Http;
using ShopForge.Shared.Shipping;

namespace ShopForge.Orders.Shipping;

// The carrier services the platform knows, kept by the operator (D-218): what each takes and where that was read. A
// service is switched off rather than deleted, because stores' methods name it; readiness names the methods left
// sending by one that is off.
internal static class PlatformCarrierServiceEndpoints
{
    // Ten metres, as for a product's sides (D-215): no parcel comes near it.
    private const int MaxSideMm = 10_000;

    public static void MapPlatformCarrierServices(this IEndpointRouteBuilder platform)
    {
        var services = platform.MapGroup("/carrier-services");

        services.MapGet("/", GetServicesAsync);
        services.MapPost("/", CreateServiceAsync);
        services.MapPut("/{key}", UpdateServiceAsync);
    }

    private static async Task<Ok<List<CarrierServiceResponse>>> GetServicesAsync(DbContext dbContext, CancellationToken cancellationToken)
    {
        var services = await dbContext.Set<CarrierService>().AsNoTracking().OrderBy(service => service.Key).ToListAsync(cancellationToken);

        return TypedResults.Ok(services.Select(CarrierServiceResponse.From).ToList());
    }

    private static async Task<Results<Created<CarrierServiceResponse>, ValidationProblem, ProblemHttpResult>> CreateServiceAsync(
        NewCarrierServiceRequest request,
        DbContext dbContext,
        IEnumerable<IShippingProvider> providers,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request.Terms)
            .Check(
                request.Key is { Length: > 0 and <= CarrierService.MaxKeyLength } key && key.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character == '-'),
                "key",
                $"A key is lower-case letters, digits and hyphens, up to {CarrierService.MaxKeyLength} characters.")
            .Check(providers.Any(provider => provider.Key == request.ProviderKey), "providerKey", "That shipping provider is not one this platform has.")
            .Check(request.Kind is not null, "kind", "Say what kind of service it is: locker, pickupPoint or home.")
            .Check(request.Country is { Length: 2 } country && country.All(char.IsAsciiLetter), "country", "A country is its two-letter code, such as CZ.");

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        if (await dbContext.Set<CarrierService>().AnyAsync(service => service.Key == request.Key, cancellationToken))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status409Conflict, title: "A carrier service with this key already exists");
        }

        var service = new CarrierService(request.Key!, request.ProviderKey!, request.Kind!.Value, request.Country!);
        Apply(service, request.Terms);
        dbContext.Add(service);
        audit.Record("carrier-service.added", service.Key, Snapshot(service));
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Created($"/api/platform/carrier-services/{service.Key}", CarrierServiceResponse.From(service));
    }

    // The limits, the name, the source and whether it is in use. What it is, whose it is and where it goes are what
    // make it this service; a different one is a new row.
    private static async Task<Results<Ok<CarrierServiceResponse>, ValidationProblem, NotFound>> UpdateServiceAsync(
        string key,
        CarrierServiceTerms request,
        DbContext dbContext,
        IAuditLog audit,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);

        if (errors.Any)
        {
            return errors.ToProblem();
        }

        var service = await dbContext.Set<CarrierService>().SingleOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (service is null)
        {
            return TypedResults.NotFound();
        }

        var before = Snapshot(service);
        Apply(service, request);
        audit.Record("carrier-service.changed", service.Key, new { Before = before, After = Snapshot(service) });
        await dbContext.SaveChangesAsync(cancellationToken);

        return TypedResults.Ok(CarrierServiceResponse.From(service));
    }

    private static RequestErrors Validate(CarrierServiceTerms? terms)
    {
        var largest = terms?.Largest?.ToSides();
        var smallest = terms?.Smallest?.ToSides();

        return new RequestErrors()
            .Check(terms?.Name is { } name && !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100, "name", "A name is required (up to 100 characters).")
            .Check(terms?.MaxWeightGrams is > 0, "maxWeightGrams", "The heaviest parcel weighs something.")
            .Check(largest is not null, "largest", $"The largest parcel is three sides, each a whole number of millimetres from 1 to {MaxSideMm}.")
            .Check(smallest is not null, "smallest", $"The smallest parcel is three sides, each a whole number of millimetres from 1 to {MaxSideMm}.")
            .Check(largest is null || smallest is null || smallest.Value.FitIn(largest.Value), "smallest", "The smallest parcel cannot be larger than the largest.")
            .Check(terms?.MaxSumOfSidesMm is null or > 0, "maxSumOfSidesMm", "A limit on the sides' sum is more than nothing.")
            .Check(terms?.LargestVolumeMm3 is > 0, "largestVolumeMm3", "The largest parcel holds something.")
            .Check(
                largest is null || terms?.LargestVolumeMm3 is not { } volume || volume <= Roomiest(largest.Value, terms.MaxSumOfSidesMm),
                "largestVolumeMm3",
                "No parcel within these sides holds that much.")
            .Check(terms?.MaxParcels is null or >= 1, "maxParcels", "A limit on parcels to an order is at least one.")
            .Check(terms?.Source is { } source && !string.IsNullOrWhiteSpace(source) && source.Trim().Length <= CarrierService.MaxSourceLength, "source", $"Say where these limits come from (up to {CarrierService.MaxSourceLength} characters).")
            .Check(terms?.SourceDate is not null, "sourceDate", "Say when they were read.");
    }

    // The most a parcel within these sides can hold: their own volume, and for sides that may add up to only so much, the
    // cube a third of that on each side, which holds more than any other box whose sides add up to the same.
    private static long Roomiest(Sides largest, int? maxSumOfSidesMm) =>
        maxSumOfSidesMm is { } sum ? Math.Min(largest.Volume, (long)(sum / 3) * (sum / 3) * (sum / 3)) : largest.Volume;

    private static void Apply(CarrierService service, CarrierServiceTerms terms)
    {
        service.Describe(terms.Name!, terms.IsActive, terms.Source!, terms.SourceDate!.Value);
        service.Limit(terms.MaxWeightGrams!.Value, terms.Largest!.ToSides()!.Value, terms.MaxSumOfSidesMm, terms.Smallest!.ToSides()!.Value, terms.LargestVolumeMm3!.Value, terms.MaxParcels);
    }

    private static object Snapshot(CarrierService service) =>
        new
        {
            service.Name,
            service.IsActive,
            service.MaxWeightGrams,
            Largest = new[] { service.LongestMm, service.MiddleMm, service.ShortestMm },
            service.MaxSumOfSidesMm,
            Smallest = new[] { service.MinLongestMm, service.MinMiddleMm, service.MinShortestMm },
            service.LargestVolumeMm3,
            service.MaxParcels,
            service.Source,
            service.SourceDate,
        };
}

// Three sides as somebody measured them, in any order.
internal sealed record SizeRequest(int? LengthMm, int? WidthMm, int? HeightMm)
{
    public Sides? ToSides() =>
        LengthMm is { } length && WidthMm is { } width && HeightMm is { } height && new[] { length, width, height }.All(side => side is > 0 and <= 10_000)
            ? Sides.Of(length, width, height)
            : null;
}

internal sealed record CarrierServiceTerms(
    string? Name,
    bool IsActive,
    int? MaxWeightGrams,
    SizeRequest? Largest,
    int? MaxSumOfSidesMm,
    SizeRequest? Smallest,
    long? LargestVolumeMm3,
    int? MaxParcels,
    string? Source,
    DateOnly? SourceDate);

internal sealed record NewCarrierServiceRequest(
    string? Key,
    string? ProviderKey,
    CarrierServiceKind? Kind,
    string? Country,
    string? Name,
    bool IsActive,
    int? MaxWeightGrams,
    SizeRequest? Largest,
    int? MaxSumOfSidesMm,
    SizeRequest? Smallest,
    long? LargestVolumeMm3,
    int? MaxParcels,
    string? Source,
    DateOnly? SourceDate)
{
    public CarrierServiceTerms Terms => new(Name, IsActive, MaxWeightGrams, Largest, MaxSumOfSidesMm, Smallest, LargestVolumeMm3, MaxParcels, Source, SourceDate);
}

internal sealed record SizeResponse(int LengthMm, int WidthMm, int HeightMm)
{
    public static SizeResponse From(Sides sides) => new(sides.Longest, sides.Middle, sides.Shortest);
}

internal sealed record CarrierServiceResponse(
    string Key,
    string ProviderKey,
    string Name,
    CarrierServiceKind Kind,
    string Country,
    bool IsActive,
    int MaxWeightGrams,
    SizeResponse Largest,
    int? MaxSumOfSidesMm,
    SizeResponse Smallest,
    long LargestVolumeMm3,
    int? MaxParcels,
    string Source,
    DateOnly SourceDate)
{
    public static CarrierServiceResponse From(CarrierService service) =>
        new(
            service.Key,
            service.ProviderKey,
            service.Name,
            service.Kind,
            service.Country,
            service.IsActive,
            service.MaxWeightGrams,
            SizeResponse.From(service.Largest),
            service.MaxSumOfSidesMm,
            SizeResponse.From(service.Smallest),
            service.LargestVolumeMm3,
            service.MaxParcels,
            service.Source,
            service.SourceDate);
}
