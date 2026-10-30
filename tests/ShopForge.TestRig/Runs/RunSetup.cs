using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using ShopForge.TestRig.Comgate;
using ShopForge.TestRig.Mail;

namespace ShopForge.TestRig.Runs;

// A run made the way a merchant would make it, through the API and nothing else: the operator creates a company, its
// owner takes up the invitation the stand-in for Mailgun received, and the owner opens two stores, connects Comgate,
// lists a T-shirt in three sizes and a mug, stocks them and publishes; and, when asked, opens the tool shop beside them. Nothing is written to the database behind the
// API's back, so a run is also a check that the platform can still be set up from nothing (D-203).
internal sealed class RunSetup(RigOptions options, FakeComgate comgate, FakeMailgun mailgun, RunBook runs)
{
    // A one-pixel PNG: the store has a logo, which publication asks for, and nothing more.
    private static readonly byte[] Logo = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static readonly object Theme = new { primaryColor = "#1F4E79", secondaryColor = "#FFFFFF", borderRadius = 6 };

    private static readonly object Company = new
    {
        legalName = "Run Company s.r.o.",
        line1 = "Testovací 1",
        city = "Brno",
        postalCode = "602 00",
        country = "CZ",
        registrationNumber = "12345678",
        vatNumber = "CZ12345678",
    };

    private const string Spreadsheet = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<Run> StartAsync(bool withToolShop, CancellationToken cancellationToken)
    {
        var id = "r" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
        var domain = $"{id}.e2e.test";
        var owner = new Person($"owner@{domain}", Password());
        var merchant = $"RUN-{id.ToUpperInvariant()}";
        var secret = Password();

        using var anyone = new ApiSession(options.ApiOrigin);
        await RefuseAnythingButTheTestApiAsync(anyone, cancellationToken);

        using var platform = new ApiSession(options.ApiOrigin);
        await platform.PostAsync("api/platform/auth/login", new { email = options.OperatorEmail, password = options.OperatorPassword }, HttpStatusCode.OK, cancellationToken);
        var tenant = await platform.PostAsync("api/platform/tenants", new { name = $"Run {id}", ownerEmail = owner.Email }, HttpStatusCode.Created, cancellationToken);

        using var admin = new ApiSession(options.ApiOrigin);
        var token = await InvitationTokenAsync(domain, owner.Email, cancellationToken);
        await admin.PostAsync("api/admin/invitations/accept", new { token, password = owner.Password }, HttpStatusCode.OK, cancellationToken);
        await admin.PostAsync("api/admin/auth/login", new { email = owner.Email, password = owner.Password }, HttpStatusCode.OK, cancellationToken);

        comgate.Register(merchant, secret);
        var stores = new List<RunStore>();

        foreach (var letter in new[] { "a", "b" })
        {
            stores.Add(await OpenStoreAsync(admin, $"Run {id} {letter.ToUpperInvariant()}", $"{id}-{letter}", merchant, secret, cancellationToken));
        }

        var products = new List<RunProduct>
        {
            await ProductAsync(admin, stores, $"{id.ToUpperInvariant()}-TEE", "T-shirt", "t-shirt", 390m, ["S", "M", "L"], stock: 5, cancellationToken),
            await ProductAsync(admin, stores, $"{id.ToUpperInvariant()}-MUG", "Mug", "mug", 190m, [], stock: 10, cancellationToken),
        };

        var toolShop = withToolShop ? await OpenStoreAsync(admin, $"Run {id} Kladívko", $"{id}-c", merchant, secret, cancellationToken) : null;

        if (toolShop is not null)
        {
            await StockToolShopAsync(admin, anyone, toolShop, cancellationToken);
        }

        foreach (var store in toolShop is null ? stores : [.. stores, toolShop])
        {
            await admin.PostAsync($"api/admin/stores/{store.Id}/publish", null, HttpStatusCode.OK, cancellationToken);
        }

        var run = new Run(id, domain, tenant.GetProperty("id").GetGuid(), owner, new ComgateAccount(merchant), stores, products, toolShop, DateTimeOffset.UtcNow);
        runs.Add(run);

        return run;
    }

    // The rig writes through whatever API it is pointed at, so before writing anything it asks that API to say it is an
    // end-to-end one, using the database the readings come from. A development or a real deployment says neither.
    private async Task RefuseAnythingButTheTestApiAsync(ApiSession anyone, CancellationToken cancellationToken)
    {
        JsonElement marker;

        try
        {
            marker = await anyone.GetAsync("api/e2e", cancellationToken);
        }
        catch (RunSetupException exception)
        {
            throw new RunSetupException($"The API at {options.ApiOrigin} is not an end-to-end API: {exception.Message}");
        }

        var database = new NpgsqlConnectionStringBuilder(options.Database).Database;

        if (marker.GetProperty("environment").GetString() != RigOptions.EnvironmentName || marker.GetProperty("database").GetString() != database)
        {
            throw new RunSetupException($"The API at {options.ApiOrigin} is not the end-to-end API over {database}: {marker}");
        }
    }

    private async Task<string> InvitationTokenAsync(string domain, string owner, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var link = mailgun.To(domain, owner)
                .SelectMany(letter => letter.Links)
                .FirstOrDefault(candidate => candidate.Contains("invitations/accept?token=", StringComparison.Ordinal));

            if (link is not null)
            {
                return Uri.UnescapeDataString(link[(link.IndexOf("token=", StringComparison.Ordinal) + "token=".Length)..]);
            }

            await Task.Delay(200, cancellationToken);
        }

        throw new RunSetupException($"No invitation reached {owner} within twenty seconds.");
    }

    private static async Task<RunStore> OpenStoreAsync(
        ApiSession admin, string name, string label, string merchant, string secret, CancellationToken cancellationToken)
    {
        var store = await admin.PostAsync(
            "api/admin/stores", new { name, hostName = label, currency = "CZK", culture = "cs-CZ", theme = Theme }, HttpStatusCode.Created, cancellationToken);
        var id = store.GetProperty("id").GetGuid();
        var path = $"api/admin/stores/{id}";

        await admin.PutFileAsync($"{path}/logo", Logo, "logo.png", "image/png", HttpStatusCode.NoContent, cancellationToken);
        await admin.PutAsync(path, new { name, currency = "CZK", culture = "cs-CZ", theme = Theme, returnWindowDays = 14, company = Company }, HttpStatusCode.OK, cancellationToken);
        await admin.PutAsync($"{path}/provider-connections/comgate", new { merchantId = merchant, environment = "Test", isActive = true }, HttpStatusCode.OK, cancellationToken);
        await admin.PutAsync($"{path}/provider-connections/comgate/secret", new { secret }, HttpStatusCode.NoContent, cancellationToken);
        await admin.PostAsync($"{path}/payment-methods", new { name = "Comgate", providerKey = "comgate", isActive = true }, HttpStatusCode.Created, cancellationToken);

        // A store starts with no delivery method (D-223). The journeys collect from the store, so the rig sets that up as a
        // merchant would, under the code they choose it by.
        await admin.PostAsync(
            $"{path}/shipping-methods",
            new { name = "Personal pickup", providerKey = "manual", price = 0m, vatRate = 21m, isActive = true, requiresPickupPoint = false },
            HttpStatusCode.Created,
            cancellationToken);

        return new RunStore(id, name, store.GetProperty("primaryHostName").GetString()!);
    }

    // The development data's Czech tool shop, built as a merchant would build it (D-227): the API describes the tree and
    // the attributes, which are made through the admin API, and hands out the workbook every product is imported from.
    private static async Task StockToolShopAsync(ApiSession admin, ApiSession anyone, RunStore store, CancellationToken cancellationToken)
    {
        var path = $"api/admin/stores/{store.Id}";
        var catalogue = await anyone.GetAsync("api/e2e/catalogues/tool-shop", cancellationToken);
        var categories = new Dictionary<string, Guid>();

        foreach (var (category, index) in catalogue.GetProperty("categories").EnumerateArray().Select((category, index) => (category, index)))
        {
            var parent = category.GetProperty("parentSlug").GetString();
            var created = await admin.PostAsync(
                $"{path}/categories",
                new
                {
                    name = category.GetProperty("name").GetString(),
                    slug = category.GetProperty("slug").GetString(),
                    sortOrder = index,
                    parentId = parent is null ? (Guid?)null : categories[parent],
                    searchKeywords = category.GetProperty("keywords").GetString(),
                },
                HttpStatusCode.Created,
                cancellationToken);
            categories[category.GetProperty("slug").GetString()!] = created.GetProperty("id").GetGuid();
        }

        var offered = new Dictionary<string, List<Guid>>();

        foreach (var (attribute, index) in catalogue.GetProperty("attributes").EnumerateArray().Select((attribute, index) => (attribute, index)))
        {
            var created = await admin.PostAsync(
                $"{path}/attributes",
                new
                {
                    code = attribute.GetProperty("code").GetString(),
                    name = attribute.GetProperty("name").GetString(),
                    type = attribute.GetProperty("type").GetString(),
                    unit = attribute.GetProperty("unit").GetString(),
                    isFilterable = attribute.GetProperty("filterable").GetBoolean(),
                    isVisibleOnProductPage = true,
                    sortOrder = index,
                    isSearchable = attribute.GetProperty("searchable").GetBoolean(),
                    options = attribute.GetProperty("options").EnumerateArray().Select(option => option.GetString()).ToArray(),
                },
                HttpStatusCode.Created,
                cancellationToken);

            foreach (var category in attribute.GetProperty("filterIn").EnumerateArray().Select(category => category.GetString()!))
            {
                (offered.TryGetValue(category, out var ids) ? ids : offered[category] = []).Add(created.GetProperty("id").GetGuid());
            }
        }

        foreach (var (category, ids) in offered)
        {
            await admin.PutAsync($"{path}/categories/{categories[category]}/attributes", new { attributeIds = ids }, HttpStatusCode.OK, cancellationToken);
        }

        var workbook = await anyone.GetFileAsync("api/e2e/catalogues/tool-shop/products.xlsx", cancellationToken);
        var report = await admin.PostFileAsync($"{path}/import", workbook, "tool-shop.xlsx", Spreadsheet, HttpStatusCode.OK, cancellationToken);

        if (report.GetProperty("invalid").GetInt32() > 0 || report.GetProperty("failed").GetInt32() > 0)
        {
            throw new RunSetupException($"The tool shop's products did not all import: {report}");
        }
    }

    // One product of the company's, sold in both stores. Sizes make it a product with variants, each stocked.
    private static async Task<RunProduct> ProductAsync(
        ApiSession admin, List<RunStore> stores, string sku, string name, string slug, decimal price, string[] sizes, int stock, CancellationToken cancellationToken)
    {
        var first = sizes.Length > 0 ? $"{sku}-{sizes[0]}" : sku;
        var product = await admin.PostAsync("api/admin/products", new { sku = first, ean = (string?)null, weightGrams = 300, lengthMm = 250, widthMm = 200, heightMm = 100 }, HttpStatusCode.Created, cancellationToken);
        var productId = product.GetProperty("id").GetGuid();
        var defaultVariant = product.GetProperty("variants")[0].GetProperty("id").GetGuid();
        var variants = new List<RunVariant> { new(defaultVariant, first, sizes.FirstOrDefault(), stock) };

        if (sizes.Length > 0)
        {
            await admin.PutAsync(
                $"api/admin/products/{productId}/options",
                new { names = new[] { "Size" }, values = new Dictionary<Guid, string[]> { [defaultVariant] = [sizes[0]] } },
                HttpStatusCode.OK,
                cancellationToken);

            foreach (var size in sizes.Skip(1))
            {
                var added = await admin.PostAsync(
                    $"api/admin/products/{productId}/variants",
                    new { sku = $"{sku}-{size}", ean = (string?)null, weightGrams = 300, optionValues = new[] { size }, lengthMm = 250, widthMm = 200, heightMm = 100 },
                    HttpStatusCode.Created,
                    cancellationToken);
                variants.Add(new RunVariant(VariantId(added, $"{sku}-{size}"), $"{sku}-{size}", size, stock));
            }
        }

        foreach (var variant in variants)
        {
            await admin.PutAsync($"api/admin/stock/{variant.Id}", new { quantity = stock }, HttpStatusCode.OK, cancellationToken);
        }

        var listings = new Dictionary<Guid, Guid>();

        foreach (var store in stores)
        {
            var listing = await admin.PostAsync(
                $"api/admin/stores/{store.Id}/products",
                new { productId, name, slug, description = $"{name} for the run.", price, vatRate = 21m, isVisible = true, sortOrder = 0 },
                HttpStatusCode.Created,
                cancellationToken);
            listings[store.Id] = listing.GetProperty("id").GetGuid();
        }

        return new RunProduct(productId, name, slug, price, variants, listings);
    }

    // Whether the answer is the variant or the product it now belongs to, the variant is found by its code.
    private static Guid VariantId(JsonElement added, string sku) =>
        added.TryGetProperty("variants", out var all)
            ? all.EnumerateArray().Single(variant => variant.GetProperty("sku").GetString() == sku).GetProperty("id").GetGuid()
            : added.GetProperty("id").GetGuid();

    private static string Password() => "Run-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
}
