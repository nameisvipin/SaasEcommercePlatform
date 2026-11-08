using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using SaasEcommerce.Core.Common;
using SaasEcommerce.Core.Entities;
using SaasEcommerce.Core.Interfaces;

namespace SaasEcommerce.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    private readonly ITenantContext _tenantContext;

    public Guid? CurrentTenantId => _tenantContext.TenantId;
    public bool IsSuperAdmin => _tenantContext.IsSuperAdminContext;

    public AppDbContext(
        DbContextOptions<AppDbContext> options,
        ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<StorefrontTheme> StorefrontThemes => Set<StorefrontTheme>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Master Tenants Table Configuration
        modelBuilder.Entity<Tenant>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Subdomain).IsUnique();
            b.HasIndex(x => x.CustomDomain).IsUnique().HasFilter("\"CustomDomain\" IS NOT NULL");
            b.Property(x => x.StoreName).HasMaxLength(150).IsRequired();
            b.Property(x => x.Subdomain).HasMaxLength(63).IsRequired();
            b.Property(x => x.CustomDomain).HasMaxLength(255);
        });

        // 2. StorefrontTheme Configuration (JSONB)
        modelBuilder.Entity<StorefrontTheme>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.TenantId).IsUnique();
            b.Property(x => x.LayoutConfigJson).HasColumnType("jsonb");
            b.HasOne<Tenant>()
             .WithOne(t => t.Theme)
             .HasForeignKey<StorefrontTheme>(x => x.TenantId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // 3. Users Configuration (Email is unique PER TENANT so a buyer can sign up on multiple client stores)
        modelBuilder.Entity<AppUser>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.Property(x => x.Email).HasMaxLength(255).IsRequired();
            b.HasQueryFilter(u => IsSuperAdmin || u.TenantId == CurrentTenantId);
        });

        // 4. Automatically apply Global Query Filter & Composite Tenant Index to ALL ITenantScoped Entities
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ITenantScoped).IsAssignableFrom(entityType.ClrType))
            {
                var method = typeof(AppDbContext)
                    .GetMethod(nameof(ApplyTenantFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(entityType.ClrType);

                method.Invoke(this, new object[] { modelBuilder });
            }
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, ITenantScoped
    {
        modelBuilder.Entity<TEntity>().HasIndex(x => x.TenantId);
        modelBuilder.Entity<TEntity>().HasQueryFilter(x => IsSuperAdmin || x.TenantId == CurrentTenantId);
    }
}