using Microsoft.EntityFrameworkCore;

namespace FutebolAtemporal.Infrastructure.Persistence;

// Each context type has its own EF migrations and snapshot. Both share the application model.
public sealed class PostgresAppDbContext(DbContextOptions<PostgresAppDbContext> options) : AppDbContext(options);
