using Npgsql;

namespace ShopForge.IntegrationTests.Catalog;

// A form as it could be before every form had to be weighed and measured (D-216). The API takes nothing less any
// more, so the row is emptied underneath it, the way the migration left every form it found.
internal static class FormsFromBefore
{
    public static Task UnsizeAsync(ShopForgeApiFactory factory, Guid productId) =>
        ExecuteAsync(factory, "UPDATE catalog.product_variants SET length_mm = NULL, width_mm = NULL, height_mm = NULL WHERE product_id = @id", productId);

    public static Task UnsizeFormAsync(ShopForgeApiFactory factory, Guid variantId) =>
        ExecuteAsync(factory, "UPDATE catalog.product_variants SET length_mm = NULL, width_mm = NULL, height_mm = NULL WHERE id = @id", variantId);

    public static Task UnweighAsync(ShopForgeApiFactory factory, Guid productId) =>
        ExecuteAsync(factory, "UPDATE catalog.product_variants SET weight_grams = NULL WHERE product_id = @id", productId);

    private static async Task ExecuteAsync(ShopForgeApiFactory factory, string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(factory.DatabaseConnectionString);
        await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);

        Assert.True(await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken) > 0);
    }
}
