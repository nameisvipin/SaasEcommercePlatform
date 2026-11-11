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

    // Phase 3 Buyer, Cart, Promo & Order Tables
    public DbSet<BuyerAddress> BuyerAddresses => Set<BuyerAddress>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<Coupon> Coupons => Set<Coupon>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    // Phase 4 Logistics, Shipments & RTO Tables
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<RtoDisputeClaim> RtoDisputeClaims => Set<RtoDisputeClaim>();

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

        // 7a. BuyerAddress Table
        modelBuilder.Entity<BuyerAddress>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.UserId });
            b.Property(x => x.PinCode).HasMaxLength(10).IsRequired();
            b.Property(x => x.StateCode).HasMaxLength(5).IsRequired();
        });

        // 7b. CartItem Table
        modelBuilder.Entity<CartItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.UserId, x.ProductVariantId }).IsUnique();
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
            b.HasOne(x => x.ProductVariant).WithMany().HasForeignKey(x => x.ProductVariantId).OnDelete(DeleteBehavior.Cascade);
        });

        // 7c. Coupon Table
        modelBuilder.Entity<Coupon>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            b.Property(x => x.Code).HasMaxLength(40).IsRequired();
            b.Property(x => x.DiscountValue).HasPrecision(12, 2);
            b.Property(x => x.MinOrderAmount).HasPrecision(12, 2);
            b.Property(x => x.MaxDiscountCap).HasPrecision(12, 2);
        });

        // 7d. Order & OrderItem Tables
        modelBuilder.Entity<Order>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.OrderNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.BuyerId });
            b.Property(x => x.OrderNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.InvoiceNumber).HasMaxLength(50).IsRequired();
            b.Property(x => x.SubTotalTaxable).HasPrecision(12, 2);
            b.Property(x => x.TotalCgst).HasPrecision(12, 2);
            b.Property(x => x.TotalSgst).HasPrecision(12, 2);
            b.Property(x => x.TotalIgst).HasPrecision(12, 2);
            b.Property(x => x.TotalGst).HasPrecision(12, 2);
            b.Property(x => x.DiscountAmount).HasPrecision(12, 2);
            b.Property(x => x.ShippingCharge).HasPrecision(12, 2);
            b.Property(x => x.GrandTotal).HasPrecision(12, 2);
        });

        modelBuilder.Entity<OrderItem>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.OrderId });
            b.Property(x => x.UnitPriceInclusive).HasPrecision(12, 2);
            b.Property(x => x.TaxableAmount).HasPrecision(12, 2);
            b.Property(x => x.GstPercentage).HasPrecision(5, 2);
            b.Property(x => x.CgstAmount).HasPrecision(12, 2);
            b.Property(x => x.SgstAmount).HasPrecision(12, 2);
            b.Property(x => x.IgstAmount).HasPrecision(12, 2);
            b.Property(x => x.LineTotal).HasPrecision(12, 2);
            b.HasOne(x => x.Order).WithMany(o => o.Items).HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        });

        // 7e. Shipment Table
        modelBuilder.Entity<Shipment>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.AwbNumber }).IsUnique();
            b.HasIndex(x => new { x.TenantId, x.OrderId }).IsUnique();
            b.Property(x => x.AwbNumber).HasMaxLength(60).IsRequired();
            b.Property(x => x.CourierName).HasMaxLength(100).IsRequired();
            b.Property(x => x.ChargeableWeightGrams).HasPrecision(10, 2);
            b.Property(x => x.EstimatedFreightCost).HasPrecision(10, 2);
            b.Property(x => x.Checkpoints)
             .HasColumnType("jsonb")
             .HasDefaultValueSql("'[]'::jsonb");

            b.HasOne(x => x.Order)
             .WithOne()
             .HasForeignKey<Shipment>(x => x.OrderId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // 7f. RtoDisputeClaim Table
        modelBuilder.Entity<RtoDisputeClaim>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => new { x.TenantId, x.ShipmentId });
            b.Property(x => x.AwbNumber).HasMaxLength(60).IsRequired();
            b.Property(x => x.IssueType).HasMaxLength(120).IsRequired();
            b.Property(x => x.ProofMediaUrls)
             .HasColumnType("jsonb")
             .HasDefaultValueSql("'[]'::jsonb");
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