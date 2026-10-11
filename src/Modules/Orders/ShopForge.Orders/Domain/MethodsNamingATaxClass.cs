using Microsoft.EntityFrameworkCore;
using ShopForge.Shared.Tax;

namespace ShopForge.Orders.Domain;

// What the delivery methods say when a shop wants a tax class gone (D-180, D-187).
internal sealed class MethodsNamingATaxClass(DbContext dbContext) : ITaxClassUsers
{
    public async Task<string?> WhatNamesAsync(string taxClass, CancellationToken cancellationToken)
    {
        var methods = await dbContext.Set<ShippingMethod>()
            .CountAsync(method => method.TaxClass == taxClass, cancellationToken);

        return methods == 0 ? null : $"{methods} delivery {(methods == 1 ? "method is" : "methods are")} taxed as it";
    }
}
