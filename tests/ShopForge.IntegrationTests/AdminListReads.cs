using System.Net.Http.Json;
using ShopForge.Shared.Admin;

namespace ShopForge.IntegrationTests;

// Admin lists answer with a page and a total rather than a bare array (D-179). A test that only wants the
// rows says so here instead of unwrapping the envelope in twenty places.
internal static class AdminListReads
{
    public static async Task<List<T>> AdminListAsync<T>(this HttpClient admin, string url, CancellationToken cancellationToken) =>
        (await admin.GetFromJsonAsync<AdminListResponse<T>>(url, cancellationToken))!.Items;

    public static async Task<AdminListResponse<T>> AdminPageAsync<T>(this HttpClient admin, string url, CancellationToken cancellationToken) =>
        (await admin.GetFromJsonAsync<AdminListResponse<T>>(url, cancellationToken))!;
}
