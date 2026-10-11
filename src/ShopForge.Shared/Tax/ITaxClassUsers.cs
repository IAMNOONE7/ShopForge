namespace ShopForge.Shared.Tax;

// Asked before a tax class is taken away: whatever still names it says so, and says how much of it there is.
// A class that vanished from under a listing would leave it taxed by a fallback nobody chose (D-180, D-187).
public interface ITaxClassUsers
{
    Task<string?> WhatNamesAsync(string taxClass, CancellationToken cancellationToken);
}
