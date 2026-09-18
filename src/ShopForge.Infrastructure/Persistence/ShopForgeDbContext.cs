using Microsoft.EntityFrameworkCore;

namespace ShopForge.Infrastructure.Persistence;

public sealed class ShopForgeDbContext(DbContextOptions<ShopForgeDbContext> options) : DbContext(options);
