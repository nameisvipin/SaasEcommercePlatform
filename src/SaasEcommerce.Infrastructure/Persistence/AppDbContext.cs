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

    // Phase 0 Tables
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<StorefrontTheme> StorefrontThemes => Set<StorefrontTheme>();
    public DbSet<AppUser> Users => Set<AppUser>();

    // Phase 1 Catalog & Inventory Tables
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductMedia> ProductMedia => Set<ProductMedia>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 1. Master Tenants Table
        modelBuilder.Entity<Tenant>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Subdomain).IsUnique();
            b.HasIndex(x => x.CustomDomain).IsUnique().HasFilter("\"CustomDomain\" IS NOT NULL");
            b.Property(x => x.StoreName).HasMaxLength(150).IsRequired();
            b.Property(x => x.Subdomain).HasMaxLength(63).IsRequired();
            b.Property(x => x.CustomDomain).HasMaxLength(255);
        });

        // 2. StorefrontTheme Table
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

        // 3. Users Table
        modelBuilder.Entity<AppUser>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Email }).IsUnique();
            b.Property(x => x.Email).HasMaxLength(255).IsRequired();
            b.HasQueryFilter(u => IsSuperAdmin || u.TenantId == CurrentTenantId);
        });

        // 4. Category Table
        modelBuilder.Entity<Category>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            b.Property(x => x.Name).HasMaxLength(120).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(150).IsRequired();
            b.HasOne(x => x.ParentCategory)
             .WithMany(x => x.SubCategories)
             .HasForeignKey(x => x.ParentCategoryId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // 5. Product Table
        modelBuilder.Entity<Product>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Slug }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.Status, x.CategoryId });
            b.Property(x => x.Title).HasMaxLength(250).IsRequired();
            b.Property(x => x.Slug).HasMaxLength(280).IsRequired();
            b.Property(x => x.HsnCode).HasMaxLength(12).IsRequired();
            b.Property(x => x.GstPercentage).HasPrecision(5, 2);
            b.Property(x => x.BulletPoints).HasColumnType("jsonb");
            b.Property(x => x.SeoKeywords).HasColumnType("jsonb");
            b.Property(x => x.APlusContentJson).HasColumnType("jsonb");
            // b.Property(x => x.QuantityDiscounts).HasColumnType("jsonb");
            b.Property(x => x.QuantityDiscounts)
                        .HasColumnType("jsonb")
                        .HasDefaultValueSql("'[]'::jsonb");
            b.Property(x => x.AverageRating).HasPrecision(3, 2);

            b.HasOne(x => x.Category)
             .WithMany()
             .HasForeignKey(x => x.CategoryId)
             .OnDelete(DeleteBehavior.SetNull);
        });

        // 6. ProductVariant Table (Unique SKU per Tenant!)
        modelBuilder.Entity<ProductVariant>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
            b.Property(x => x.Sku).HasMaxLength(80).IsRequired();
            b.Property(x => x.Mrp).HasPrecision(12, 2);
            b.Property(x => x.SellingPrice).HasPrecision(12, 2);
            b.Property(x => x.SalePrice).HasPrecision(12, 2);
            b.Ignore(x => x.SellableStock);
            b.Ignore(x => x.IsLowStock);

            b.HasOne(x => x.Product)
             .WithMany(p => p.Variants)
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // 7. ProductMedia Table
        modelBuilder.Entity<ProductMedia>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.ProductId, x.DisplayOrder });
            b.HasOne(x => x.Product)
             .WithMany(p => p.MediaGallery)
             .HasForeignKey(x => x.ProductId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // 8. Automatically apply Global Query Filter to all ITenantScoped entities
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