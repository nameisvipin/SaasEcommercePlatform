# Phase 1: Seller Catalog, Media Studio & Inventory Management

## 1. Executive Summary & Verification State
- **Status:** Complete & Fully Verified (100%)
- **Verification Date:** 2026-11-08
- **Core Outcome:** Delivered the complete catalog management suite for merchants:
  hierarchical categories, parent-child variant SKUs, GST & courier shipping specs,
  real-time completeness scoring (0–100), zero-egress Cloudflare R2 media studio 
  pre-signed URLs, buffer-protected inventory tracking, and Holiday Mode with instant Redis edge cache invalidation.

---

## 2. Implemented FRD Modules & Capabilities
| Module | FRD Reference | Implementation Details |
| :--- | :--- | :--- |
| **Category Hierarchy** | Module 3.1 | Self-referencing parent/child categories, slug unique per tenant. |
| **Variant Catalog** | Module 3.1 | Parent `Product` + child `ProductVariant` (Size, Color, Material, Pack). Unique SKU per tenant. |
| **Tax & Logistics Specs** | Module 3.1 | Mandatory HSN code, GST % (precision 5,2), shipping dimensions (Weight, L, W, H) for courier APIs. |
| **Quality Score Engine** | Module 3.1 | Evaluates title length, description depth, 4+ bullet points, 3+ WebP images, demo video, and HSN. |
| **Zero-Egress Media** | Module 3.2 | S3-compatible pre-signed PUT URLs direct to Cloudflare R2 (`tenants/{id}/products/...`). Local fallback mode enabled. |
| **Stock & Buffer Alerts** | Module 3.4 | Atomic stock updates. `SellableStock = StockQty - BufferStockQty`. Low-stock threshold alert calculation. |
| **Holiday Mode Engine** | Module 3.4 & 4.2 | Toggles extended delivery estimates for buyers; immediately purges Redis domain cache keys. |

---

## 3. Database Schema Extensions (Migration: `Phase1SellerCatalog`)
All 4 new tables implement `ITenantScoped` and are locked down by PostgreSQL Kernel RLS policies:
Categories:      (Id, TenantId, Name, Slug, ParentCategoryId, DisplayOrder, IsActive)

Products:        (Id, TenantId, CategoryId, Title, Slug, HsnCode, GstPercentage,
BulletPoints [jsonb], SeoKeywords [jsonb], QualityScore, APlusContentJson [jsonb])

ProductVariants: (Id, TenantId, ProductId, Sku, Size, Color, Mrp, SellingPrice,
StockQty, BufferStockQty, LowStockAlertThreshold, WeightGrams, Dimensions)

ProductMedia:    (Id, TenantId, ProductId, MediaType, Url, ObjectKey, DisplayOrder)
---

## 4. Key Architectural Fix Applied
- **Npgsql 8.0 Dynamic JSON Serialization:**
  Resolved `System.InvalidCastException: Writing values of 'List<string>' is not supported for parameters having NpgsqlDbType 'Jsonb'` 
  by configuring `NpgsqlDataSourceBuilder.EnableDynamicJson()` in `Program.cs`.

---

## 5. Verified Test Suite Output (Audit Trail)
- `POST /api/v1/seller/media/presigned-url` -> HTTP 200 OK
  - Generated pre-signed PUT URL with isolated path: `tenants/b00ddadd-cf11-48d4-a90e-394add359357/products/202611/c8cc423dae5a4f35955a1f3af08d5c4e.webp`
- `POST /api/v1/seller/categories` -> HTTP 201 Created
  - Created category "Mens Streetwear" (`slug: mens-streetwear`, ID: `60fa95fb-e1d4-4cba-8735-8dcbe2fe726a`)
- `POST /api/v1/seller/products` -> HTTP 201 Created
  - Created "Heavyweight 240GSM Oversized Graphic Tee" with 2 variant SKUs and 4 media items.
  - Quality Score auto-calculated: `100/100`.
- `GET /api/v1/seller/products` -> HTTP 200 OK
  - Verified catalog listing and low-stock detection (`UT-CYBER-BLK-XL` sellableStock: 4, `isLowStock: true`).
- `PATCH /api/v1/seller/store/holiday-mode` -> HTTP 200 OK
  - Enabled Holiday Mode (+5 days) and verified instant Redis cache purge via public bootstrap endpoint.