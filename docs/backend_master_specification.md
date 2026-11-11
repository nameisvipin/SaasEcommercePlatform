# Multi-Tenant E-Commerce SaaS Platform — Master Backend Specification (Phases 0–5)

## 1. System Overview & Architectural Paradigm

This document specifies the complete architecture, database schemas, security boundaries, and API contracts for the multi-tenant, white-label e-commerce SaaS platform built with **.NET 8**, **PostgreSQL 16**, **Redis 7.2**, **PgBouncer**, **Cloudflare R2**, and **QuestPDF**.

### 1.1 High-Level Architecture
- **Logical Multi-Tenancy**: Single shared database instance utilizing shared-table multi-tenancy enforced across four defensive tiers: Edge Routing, Token Validation, ORM Global Filters, and PostgreSQL Kernel Row-Level Security (RLS).
- **Zero-Egress Media**: Cloudflare R2 object storage utilizing S3 SigV4 pre-signed PUT URLs. Media uploads stream directly from the client browser to Cloudflare R2, bypassing API bandwidth and CPU consumption.
- **BYOK (Bring-Your-Own-Key) Gateways**: Store owners supply their own payment (Razorpay) and logistics (Shiprocket) API keys. Credentials are encrypted at rest using AES-256 envelope encryption derived per tenant ID.
- **Zero-Cost Local Development**: Every cloud service (Cloudflare R2, Razorpay, Shiprocket) operates with an automated mock/sandbox fallback when placeholder credentials (`YOUR_...`) are detected.

---

## 2. Multi-Tenant Defense-in-Depth Security Matrix

Every request to the platform is evaluated through four distinct isolation tiers:

```
[Incoming Request]
       │
       ▼
┌─────────────────────────────────────────────────────────────┐
│ Tier 1: Edge & Domain Resolution Middleware                │
│ • Evaluates Host / CNAME / X-Custom-Tenant-Domain           │
│ • Sub-millisecond lookup in Redis (tenant:host:{domain})    │
│ • Rejects inactive or suspended tenants (HTTP 404 / 403)    │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ Tier 2: JWT Claims & Token Replay Guard                     │
│ • Validates tenant_id claim matches resolved ITenantContext │
│ • Validates SecurityStampVersion against DB (Device Logout) │
│ • Cross-tenant token replay returns HTTP 401 Unauthorized   │
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ Tier 3: EF Core ORM Scoping & SaveChanges Interceptor       │
│ • Global Query Filter: WHERE "TenantId" = @CurrentTenantId  │
│ • TenantSaveChangesInterceptor: Force-sets TenantId on      │
│   INSERT and prevents cross-tenant UPDATE / DELETE mutations│
└──────────────────────────────┬──────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│ Tier 4: PostgreSQL Kernel Row-Level Security (RLS)          │
│ • RlsConnectionInterceptor sets 'app.current_tenant' session│
│ • Database policy blocks raw SQL/Dapper leaks automatically │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Complete Database Schema & Entity Relationship Map

The platform schema consists of **13 relational tables**, all partitioned logically by `TenantId` (except the master `Tenants` table).

### 3.1 Relational Entity Summary

| Table | Entity | Key Responsibilities | RLS Policy |
| :--- | :--- | :--- | :--- |
| `Tenants` | `Tenant` | Tenant root record, domains, encrypted BYOK keys, Holiday Mode. | Master Table |
| `StorefrontThemes` | `StorefrontTheme` | UI/UX color palettes, typography, border-radius, modular JSON sections. | Restrictive |
| `Users` | `AppUser` | Unified user table (`PlatformSuperAdmin`, `ClientSeller`, `Buyer`). | Restrictive |
| `Categories` | `Category` | Self-referencing hierarchical category taxonomy. | Restrictive |
| `Products` | `Product` | Master product catalog, HSN, GST %, JSONB SEO/bullets, Quality Score. | Restrictive |
| `ProductVariants` | `ProductVariant` | Child SKUs, price, size, color, stock, buffer stock, shipping dimensions. | Restrictive |
| `ProductMedia` | `ProductMedia` | WebP/MP4 gallery assets referencing Cloudflare R2 object keys. | Restrictive |
| `BuyerAddresses` | `BuyerAddress` | Multi-address book with Indian 6-digit PIN validation and GST state codes. | Restrictive |
| `CartItems` | `CartItem` | Active shopping cart items and Save-for-Later wishlists. | Restrictive |
| `Coupons` | `Coupon` | Percentage/Flat promo engine with order thresholds and discount caps. | Restrictive |
| `Orders` | `Order` | Immutable financial ledger, shipping snapshot, GST split (CGST/SGST/IGST).| Restrictive |
| `OrderItems` | `OrderItem` | Line item snapshot capturing unit price, HSN, and tax at checkout. | Restrictive |
| `Shipments` | `Shipment` | AWB tracking numbers, chargeable weight, QuestPDF labels, scan checkpoints.| Restrictive |
| `RtoDisputeClaims` | `RtoDisputeClaim` | Seller claims for damaged/missing returns with proof media URLs. | Restrictive |
| `ProductReviews` | `ProductReview` | Customer star ratings, verified purchase stamp, and seller replies. | Restrictive |

### 3.2 Schema Details & Constraints

#### 1. Tenants (`Tenants`)
- `Id` (UUID, PK)
- `StoreName` (VARCHAR 150, NOT NULL)
- `Subdomain` (VARCHAR 63, UNIQUE, NOT NULL)
- `CustomDomain` (VARCHAR 255, UNIQUE NULLABLE)
- `Status` (INT: 1=Active, 2=Suspended, 3=Archived)
- `IsHolidayMode` (BOOLEAN, DEFAULT FALSE)
- `HolidayExtraDeliveryDays` (INT, DEFAULT 0)
- `Gstin` (VARCHAR 20, NULLABLE)
- `LegalBusinessName` (VARCHAR 200, NULLABLE)
- `RegisteredStateCode` (VARCHAR 5, DEFAULT '07')
- `EncryptedRazorpayKeyId` (TEXT, NULLABLE)
- `EncryptedRazorpayKeySecret` (TEXT, NULLABLE)
- `EncryptedRazorpayWebhookSecret` (TEXT, NULLABLE)
- `EncryptedShiprocketApiToken` (TEXT, NULLABLE)
- `CreatedAt`, `UpdatedAt` (TIMESTAMPTZ)

#### 2. Storefront Themes (`StorefrontThemes`)
- `Id` (UUID, PK)
- `TenantId` (UUID, FK -> Tenants.Id, UNIQUE)
- `PrimaryColor` (VARCHAR 20, DEFAULT '#111827')
- `SecondaryColor` (VARCHAR 20, DEFAULT '#EF4444')
- `FontFamily` (VARCHAR 50, DEFAULT 'Inter')
- `BorderRadius` (VARCHAR 10, DEFAULT '8px')
- `LogoUrl` (TEXT, NULLABLE)
- `FaviconUrl` (TEXT, NULLABLE)
- `LayoutConfigJson` (JSONB, DEFAULT '{}'::jsonb)

#### 3. Users (`Users`)
- `Id` (UUID, PK)
- `TenantId` (UUID NULLABLE, FK -> Tenants.Id)
- `Email` (VARCHAR 255, NOT NULL) — *Unique constraint on `(TenantId, Email)`*
- `PhoneNumber` (VARCHAR 30, NULLABLE)
- `PasswordHash` (TEXT, NOT NULL)
- `FullName` (VARCHAR 150, NOT NULL)
- `Role` (INT: 1=PlatformSuperAdmin, 2=ClientSeller, 3=Buyer)
- `IsActive` (BOOLEAN, DEFAULT TRUE)
- `SecurityStampVersion` (INT, DEFAULT 1) — *Incremented on device logout*

#### 4. Categories (`Categories`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `Name` (VARCHAR 120, NOT NULL)
- `Slug` (VARCHAR 150, NOT NULL) — *Unique constraint on `(TenantId, Slug)`*
- `Description` (TEXT, NULLABLE)
- `ParentCategoryId` (UUID NULLABLE, FK -> Categories.Id)
- `DisplayOrder` (INT, DEFAULT 0)
- `IsActive` (BOOLEAN, DEFAULT TRUE)

#### 5. Products (`Products`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `CategoryId` (UUID NULLABLE, FK -> Categories.Id)
- `Title` (VARCHAR 250, NOT NULL)
- `Slug` (VARCHAR 280, NOT NULL) — *Unique constraint on `(TenantId, Slug)`*
- `BrandName` (VARCHAR 100, NULLABLE)
- `ShortDescription` (TEXT, NULLABLE)
- `RichDescriptionHtml` (TEXT, NULLABLE)
- `HsnCode` (VARCHAR 12, NOT NULL)
- `GstPercentage` (NUMERIC 5,2, DEFAULT 18.00)
- `BulletPoints` (JSONB, DEFAULT '[]'::jsonb)
- `SeoKeywords` (JSONB, DEFAULT '[]'::jsonb)
- `APlusContentJson` (JSONB, DEFAULT '[]'::jsonb)
- `QuantityDiscounts` (JSONB, DEFAULT '[]'::jsonb)
- `HasVariants` (BOOLEAN, DEFAULT FALSE)
- `Status` (INT: 0=Draft, 1=Active, 2=Archived, 99=SuppressedByAdmin)
- `QualityScore` (INT, 0–100)
- `IsAssuredQualityBadge` (BOOLEAN, DEFAULT FALSE)
- `ReplacementDays` (INT, DEFAULT 7)
- `AverageRating` (NUMERIC 3,2, DEFAULT 0.00)
- `ReviewCount` (INT, DEFAULT 0)
- *PostgreSQL GIN Index*: `idx_products_fts_search` on `to_tsvector('english', Title || ' ' || BrandName || ' ' || ShortDescription)`

#### 6. Product Variants (`ProductVariants`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `ProductId` (UUID, FK -> Products.Id, ON DELETE CASCADE)
- `Sku` (VARCHAR 80, NOT NULL) — *Unique constraint on `(TenantId, Sku)`*
- `Size` (VARCHAR 30, NULLABLE)
- `Color` (VARCHAR 30, NULLABLE)
- `Material` (VARCHAR 50, NULLABLE)
- `PackSize` (VARCHAR 30, NULLABLE)
- `Mrp` (NUMERIC 12,2, NOT NULL)
- `SellingPrice` (NUMERIC 12,2, NOT NULL)
- `SalePrice` (NUMERIC 12,2, NULLABLE)
- `StockQty` (INT, DEFAULT 0)
- `BufferStockQty` (INT, DEFAULT 0)
- `LowStockAlertThreshold` (INT, DEFAULT 5)
- `WeightGrams` (NUMERIC 10,2, DEFAULT 500.00)
- `LengthCm` (NUMERIC 6,2, DEFAULT 15.00)
- `WidthCm` (NUMERIC 6,2, DEFAULT 15.00)
- `HeightCm` (NUMERIC 6,2, DEFAULT 5.00)
- `IsDefault` (BOOLEAN, DEFAULT FALSE)
- `IsActive` (BOOLEAN, DEFAULT TRUE)
- *Computed in memory*: `SellableStock = Math.Max(0, StockQty - BufferStockQty)`

#### 7. Product Media (`ProductMedia`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `ProductId` (UUID, FK -> Products.Id, ON DELETE CASCADE)
- `MediaType` (INT: 1=ImageWebP, 2=VideoMp4, 3=View360)
- `Url` (TEXT, NOT NULL)
- `ObjectKey` (TEXT, NOT NULL)
- `ThumbnailUrl` (TEXT, NULLABLE)
- `AltText` (TEXT, NULLABLE)
- `DisplayOrder` (INT, DEFAULT 0)

#### 8. Buyer Addresses (`BuyerAddresses`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `UserId` (UUID, NOT NULL)
- `RecipientName` (VARCHAR 120, NOT NULL)
- `PhoneNumber` (VARCHAR 20, NOT NULL)
- `AddressLine1` (VARCHAR 255, NOT NULL)
- `AddressLine2` (VARCHAR 255, NULLABLE)
- `Landmark` (VARCHAR 150, NULLABLE)
- `PinCode` (VARCHAR 10, NOT NULL) — *Validated via regex `^[1-9][0-9]{5}$`*
- `City` (VARCHAR 100, NOT NULL)
- `State` (VARCHAR 100, NOT NULL)
- `StateCode` (VARCHAR 5, NOT NULL) — *2-digit GST state code (e.g., '07' Delhi)*
- `Tag` (INT: 1=Home, 2=Work, 3=Other)
- `IsDefaultShipping` (BOOLEAN, DEFAULT TRUE)
- `IsDefaultBilling` (BOOLEAN, DEFAULT TRUE)

#### 9. Cart Items (`CartItems`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `UserId` (UUID, NOT NULL)
- `ProductId` (UUID, FK -> Products.Id)
- `ProductVariantId` (UUID, FK -> ProductVariants.Id)
- `Quantity` (INT, DEFAULT 1)
- `IsSavedForLater` (BOOLEAN, DEFAULT FALSE) — *Unique constraint on `(TenantId, UserId, ProductVariantId)`*

#### 10. Coupons (`Coupons`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `Code` (VARCHAR 40, NOT NULL) — *Unique constraint on `(TenantId, Code)`*
- `Description` (TEXT, NULLABLE)
- `DiscountType` (INT: 1=Percentage, 2=FlatAmount, 3=FreeShipping)
- `DiscountValue` (NUMERIC 12,2, NOT NULL)
- `MinOrderAmount` (NUMERIC 12,2, DEFAULT 0.00)
- `MaxDiscountCap` (NUMERIC 12,2, NULLABLE)
- `StartsAt`, `ExpiresAt` (TIMESTAMPTZ)
- `UsageLimitTotal` (INT NULLABLE)
- `UsedCount` (INT, DEFAULT 0)
- `IsActive` (BOOLEAN, DEFAULT TRUE)

#### 11. Orders & Order Items (`Orders`, `OrderItems`)
- **Orders**:
  - `Id` (UUID, PK)
  - `TenantId` (UUID, NOT NULL)
  - `OrderNumber` (VARCHAR 50, UNIQUE, NOT NULL)
  - `InvoiceNumber` (VARCHAR 50, UNIQUE, NOT NULL)
  - `BuyerId` (UUID, NOT NULL)
  - `ShippingRecipientName`, `ShippingPhone`, `ShippingAddressFull`, `ShippingPinCode`, `ShippingState`, `ShippingStateCode`
  - `SubTotalTaxable`, `TotalCgst`, `TotalSgst`, `TotalIgst`, `TotalGst`, `DiscountAmount`, `AppliedCouponCode`, `ShippingCharge`, `GrandTotal` (NUMERIC 12,2)
  - `PaymentMethod` (INT: 1=OnlineRazorpay, 2=CashOnDelivery)
  - `PaymentStatus` (INT: 0=Pending, 1=Authorized, 2=Paid, 3=Failed, 4=Refunded)
  - `OrderStatus` (INT: 1=Ordered, 2=ReadyToShip, 3=Shipped, 4=OutForDelivery, 5=Delivered, 6=Cancelled, 7=Returned)
  - `GatewayOrderId`, `GatewayPaymentId` (TEXT, NULLABLE)
  - `PaidAt` (TIMESTAMPTZ NULLABLE)
- **OrderItems**:
  - `Id` (UUID, PK)
  - `TenantId` (UUID, NOT NULL)
  - `OrderId` (UUID, FK -> Orders.Id, ON DELETE CASCADE)
  - `ProductId`, `ProductVariantId` (UUID)
  - `ProductTitle`, `Sku`, `VariantAttributes`, `HsnCode` (VARCHAR)
  - `Quantity` (INT)
  - `UnitPriceInclusive`, `TaxableAmount`, `GstPercentage`, `CgstAmount`, `SgstAmount`, `IgstAmount`, `LineTotal` (NUMERIC)

#### 12. Shipments & RTO Claims (`Shipments`, `RtoDisputeClaims`)
- **Shipments**:
  - `Id` (UUID, PK)
  - `TenantId` (UUID, NOT NULL)
  - `OrderId` (UUID, FK -> Orders.Id, UNIQUE)
  - `OrderNumber` (VARCHAR 50, NOT NULL)
  - `CourierName` (VARCHAR 100, NOT NULL)
  - `AwbNumber` (VARCHAR 60, UNIQUE, NOT NULL)
  - `ExternalShipmentId` (VARCHAR 60, NULLABLE)
  - `ChargeableWeightGrams` (NUMERIC 10,2)
  - `LengthCm`, `WidthCm`, `HeightCm` (NUMERIC 6,2)
  - `EstimatedFreightCost` (NUMERIC 10,2)
  - `Status` (INT: 1=Manifested, 2=PickedUp, 3=InTransit, 4=OutForDelivery, 5=Delivered, 6=RtoInitiated, 7=RtoDelivered, 8=Cancelled)
  - `LiveTrackingUrl` (TEXT, NULLABLE)
  - `Checkpoints` (JSONB, DEFAULT '[]'::jsonb)
  - `PickedUpAt`, `DeliveredAt` (TIMESTAMPTZ NULLABLE)
- **RtoDisputeClaims**:
  - `Id` (UUID, PK)
  - `TenantId` (UUID, NOT NULL)
  - `OrderId`, `ShipmentId` (UUID)
  - `AwbNumber`, `IssueType`, `SellerRemarks` (TEXT)
  - `ProofMediaUrls` (JSONB, DEFAULT '[]'::jsonb)
  - `Status` (INT: 1=Submitted, 2=UnderReview, 3=ApprovedClaim, 4=Rejected)

#### 13. Customer Reviews (`ProductReviews`)
- `Id` (UUID, PK)
- `TenantId` (UUID, NOT NULL)
- `ProductId` (UUID, FK -> Products.Id, ON DELETE CASCADE)
- `BuyerId` (UUID, NOT NULL) — *Unique constraint on `(TenantId, ProductId, BuyerId)`*
- `OrderId` (UUID NULLABLE)
- `BuyerName` (VARCHAR 150, NOT NULL)
- `Rating` (INT, 1–5)
- `Title` (VARCHAR 150, NOT NULL)
- `Comment` (VARCHAR 2000, NOT NULL)
- `IsVerifiedPurchase` (BOOLEAN, DEFAULT FALSE)
- `MediaUrls` (JSONB, DEFAULT '[]'::jsonb)
- `SellerReply` (TEXT NULLABLE)
- `SellerRepliedAt` (TIMESTAMPTZ NULLABLE)
- `IsApproved` (BOOLEAN, DEFAULT TRUE)

---

## 4. Complete API Endpoint Specification (26 Endpoints)

### Common Request Headers
| Header | Requirement | Description |
| :--- | :--- | :--- |
| `Host` | Required (Production) | Client custom domain or subdomain (e.g., `shop.urbanthreads.in`). |
| `X-Custom-Tenant-Domain` | Development Header | Overrides `Host` for local curl / Postman testing (e.g., `shop.urbanthreads.in`). |
| `Authorization` | Protected Routes | Bearer JWT token (`Authorization: Bearer <token>`). |
| `Content-Type` | JSON POST/PUT/PATCH | `application/json`. |

---

### Module 1: Super-Admin & Tenancy Provisioning
Base Path: `/api/v1/superadmin/tenants`

#### 1.1 Provision New Tenant Store
- **Method / Path**: `POST /api/v1/superadmin/tenants`
- **Auth**: SuperAdmin Only
- **Request Body**:
```json
{
  "storeName": "Urban Threads",
  "subdomain": "urbanthreads",
  "customDomain": "shop.urbanthreads.in",
  "ownerEmail": "owner@urbanthreads.in",
  "ownerPassword": "StrongPassword#2026",
  "ownerFullName": "Vikram Malhotra",
  "gstin": "07AABCU9603R1ZM",
  "legalBusinessName": "Urban Threads Apparels Pvt Ltd",
  "registeredStateCode": "07",
  "rawRazorpayKeyId": "rzp_test_urban123",
  "rawRazorpayKeySecret": "secret_urban_xyz",
  "themePrimaryColor": "#111827",
  "themeSecondaryColor": "#EF4444"
}
```
- **Response**: `HTTP 201 Created`
```json
{
  "tenantId": "b00ddadd-cf11-48d4-a90e-394add359357",
  "storeName": "Urban Threads",
  "subdomain": "urbanthreads",
  "customDomain": "shop.urbanthreads.in",
  "ownerEmail": "owner@urbanthreads.in",
  "ownerUserId": "75d62f48-c32f-4a9b-b1f5-f77c8cb21cd7"
}
```

#### 1.2 Update Tenant Status (Suspend / Reactivate)
- **Method / Path**: `PATCH /api/v1/superadmin/tenants/{tenantId}/status`
- **Auth**: SuperAdmin Only
- **Request Body**: `{"status": 2}` *(1=Active, 2=Suspended, 3=Archived)*
- **Response**: `HTTP 200 OK` (Instantly invalidates Redis routing cache).

---

### Module 2: Authentication & Profile Management
Base Path: `/api/v1/auth`

#### 2.1 Authenticate / Login
- **Method / Path**: `POST /api/v1/auth/login`
- **Auth**: Public
- **Request Body**:
```json
{
  "email": "owner@urbanthreads.in",
  "password": "StrongPassword#2026"
}
```
- **Response**: `HTTP 200 OK`
```json
{
  "accessToken": "eyJhbGciOi...",
  "expiresInSeconds": 604800,
  "tokenType": "Bearer",
  "email": "owner@urbanthreads.in",
  "fullName": "Vikram Malhotra",
  "role": "ClientSeller",
  "tenantId": "b00ddadd-cf11-48d4-a90e-394add359357"
}
```

#### 2.2 Register Buyer on Storefront
- **Method / Path**: `POST /api/v1/auth/register`
- **Auth**: Public (Requires domain header)
- **Request Body**:
```json
{
  "email": "customer@gmail.com",
  "phoneNumber": "9876543210",
  "fullName": "Rahul Sharma",
  "password": "BuyerPassword#2026"
}
```
- **Response**: `HTTP 201 Created` (Issues 7-day JWT scoped to store).

#### 2.3 Current User Profile
- **Method / Path**: `GET /api/v1/auth/me`
- **Auth**: Bearer Token
- **Response**: `HTTP 200 OK` (`id`, `email`, `fullName`, `role`, `tenantId`).

#### 2.4 Force Logout of All Devices
- **Method / Path**: `POST /api/v1/auth/logout-all-devices`
- **Auth**: Bearer Token
- **Response**: `HTTP 200 OK` (Increments `SecurityStampVersion`, instantly revoking all active JWTs).

---

### Module 3: Public Buyer Storefront & Discovery
Base Path: `/api/v1/storefront`

#### 3.1 Storefront Bootstrap
- **Method / Path**: `GET /api/v1/storefront/bootstrap`
- **Auth**: Public
- **Response**: `HTTP 200 OK`
```json
{
  "tenantId": "b00ddadd-cf11-48d4-a90e-394add359357",
  "storeName": "Urban Threads",
  "isHolidayMode": true,
  "holidayExtraDeliveryDays": 5,
  "theme": {
    "primaryColor": "#0F172A",
    "secondaryColor": "#10B981",
    "fontFamily": "Plus Jakarta Sans",
    "borderRadius": "12px",
    "logoUrl": "https://cdn.yourplatform.com/tenants/urban/logo.webp",
    "layoutConfig": "{\"sections\": [{\"type\": \"HeroBanner\"}]}"
  }
}
```

#### 3.2 Category Navigation Tree
- **Method / Path**: `GET /api/v1/storefront/categories`
- **Auth**: Public (Cached in Redis for 30 minutes)
- **Response**: Array of active categories with parent/child hierarchies.

#### 3.3 Auto-Suggest Typeahead Search
- **Method / Path**: `GET /api/v1/storefront/search/suggest?q=cyber`
- **Auth**: Public (Sub-15ms execution)
- **Response**: `HTTP 200 OK` (Top 8 matching products with titles, thumbnails, and starting prices).

#### 3.4 Faceted Product Catalog Search
- **Method / Path**: `GET /api/v1/storefront/products`
- **Query Parameters**:
  - `q`: Search keyword (PostgreSQL FTS + ILike fallback)
  - `categorySlug`: Category slug filter
  - `brand`: Brand name
  - `minPrice`, `maxPrice`: Numeric price range
  - `minDiscount`: Minimum discount percentage (e.g., `30` for $\ge 30\%$)
  - `minRating`: Minimum average rating (1.0 to 5.0)
  - `inStockOnly`: Boolean (`true` filters for `SellableStock > 0`)
  - `assuredOnly`: Boolean (`true` filters for `IsAssuredQualityBadge = true`)
  - `sortBy`: `price_asc`, `price_desc`, `rating`, `newest` (Default)
  - `page`, `pageSize`: Pagination parameters
- **Response**: `HTTP 200 OK` (Includes `totalCount`, `items`, and dynamically extended `estimatedDeliveryDays` if Holiday Mode is active).

#### 3.5 Product Detail Page (PDP)
- **Method / Path**: `GET /api/v1/storefront/products/{slug}`
- **Auth**: Public (Cached in Redis `tenant:{id}:pdp:{slug}` for 15 minutes)
- **Response**: Full PDP payload with media gallery, MP4 videos, variant matrix, quantity discount tiers, dynamic stock badges (`In Stock`, `Only X left!`, `Out of Stock`), and trust signals.

#### 3.6 WhatsApp & Social OpenGraph SEO Preview
- **Method / Path**: `GET /api/v1/storefront/seo/products/{slug}`
- **Auth**: Public
- **Response**: `text/html` server-rendered HTML shell containing `<meta property="og:title">`, `<meta property="og:image">`, `<meta property="product:price:amount">`, and Twitter cards.

#### 3.7 PIN Code Serviceability & Delivery ETA Check
- **Method / Path**: `GET /api/v1/storefront/serviceability?pinCode=110058&weightGrams=1000`
- **Auth**: Public
- **Response**: `HTTP 200 OK` (`isServiceable`, `isCodAvailable`, `estimatedDeliveryDays`, `estimatedShippingRateInr`, `recommendedCourier`).

---

### Module 4: Buyer Account, Cart & Checkout
Base Path: `/api/v1/buyer`

#### 4.1 Address Book Management
- **Add Address**: `POST /api/v1/buyer/addresses`
  - **Body**: `recipientName`, `phoneNumber`, `addressLine1`, `pinCode` (6-digit Indian PIN regex validated), `city`, `state`, `stateCode` (2-digit GST code), `tag` (1=Home, 2=Work).
- **List Addresses**: `GET /api/v1/buyer/addresses`

#### 4.2 Shopping Cart & Wishlist
- **Upsert Item**: `POST /api/v1/buyer/cart/items`
  - **Body**: `{"productVariantId": "guid", "quantity": 2, "isSavedForLater": false}`
  - **Validation**: Enforces `quantity <= variant.SellableStock`.
- **Get Cart Summary**: `GET /api/v1/buyer/cart?couponCode=FEST20`
  - **Response**: Active items, saved-for-later items, subtotal, coupon deduction, and final payable amount.

#### 4.3 Atomic Checkout & BYOK Gateway Order
- **Method / Path**: `POST /api/v1/buyer/checkout`
- **Auth**: Buyer
- **Request Body**:
```json
{
  "shippingAddressId": "3a2af4c3-a443-4eff-85a3-cf4b8b014344",
  "paymentMethod": 1,
  "couponCode": "FEST20"
}
```
- **Execution**: Wrapped inside EF Core `CreateExecutionStrategy()` to support connection retries. Atomically deducts inventory, calculates intra-state (CGST+SGST) vs. inter-state (IGST) tax, decrypts merchant Razorpay keys via `ITenantSecretProtector`, and generates `order_rzp_...`.
- **Response**: `HTTP 201 Created` (`orderId`, `orderNumber`, `invoiceNumber`, `grandTotal`, `razorpayCheckout`).

#### 4.4 Verify Razorpay Payment Signature
- **Method / Path**: `POST /api/v1/buyer/checkout/verify-payment`
- **Auth**: Buyer
- **Request Body**:
```json
{
  "orderId": "483402e7-d8f2-456a-a430-23f3a432dc44",
  "razorpayOrderId": "order_rzp_97b5f67d4fa740",
  "razorpayPaymentId": "pay_test_998877",
  "razorpaySignature": "sandbox_test_signature"
}
```
- **Execution**: Computes `HMAC-SHA256(order_id + "|" + payment_id, merchant_secret)`. Updates status to `Paid`.

#### 4.5 Download GST Tax Invoice PDF
- **Method / Path**: `GET /api/v1/buyer/orders/{orderId}/invoice`
- **Auth**: Buyer / Seller
- **Response**: `application/pdf` (QuestPDF vector document rendered in `<20ms` at ₹0 API cost).

#### 4.6 Buyer Live Tracking Timeline
- **Method / Path**: `GET /api/v1/buyer/orders/{orderId}/tracking`
- **Auth**: Buyer
- **Response**: `HTTP 200 OK` (Reverse-chronological timeline of courier scan checkpoints).

---

### Module 5: Seller Operations & Management
Base Path: `/api/v1/seller`
Authorization Policy: `SellerOnly`

#### 5.1 Cloudflare R2 Pre-Signed Upload Studio
- **Method / Path**: `POST /api/v1/seller/media/presigned-url`
- **Request Body**: `{"fileName": "tee.webp", "contentType": "image/webp"}`
- **Response**: `HTTP 200 OK` (Returns pre-signed PUT URL with path isolated to `tenants/{tenantId}/products/{yyyyMM}/{guid}.webp`).

#### 5.2 Category Creation
- **Method / Path**: `POST /api/v1/seller/categories`
- **Request Body**: `{"name": "Mens Streetwear", "description": "...", "displayOrder": 1}`

#### 5.3 Product & Variant Catalog Creation
- **Method / Path**: `POST /api/v1/seller/products`
- **Features**: Accepts multiple SKUs, HSN codes, GST %, JSONB bullets, SEO tags, media gallery, and volume discount tiers. Automatically calculates **QualityScore (0–100)** on save.

#### 5.4 Inventory Stock & Safety Buffer Updates
- **Method / Path**: `PATCH /api/v1/seller/inventory/{variantId}`
- **Request Body**: `{"stockQty": 25, "bufferStockQty": 3, "lowStockAlertThreshold": 5}`
- **Execution**: Immediately evicts the parent product's PDP cache in Redis.

#### 5.5 Holiday Mode Engine
- **Method / Path**: `PATCH /api/v1/seller/store/holiday-mode`
- **Request Body**: `{"isHolidayMode": true, "extraDeliveryDays": 5}`
- **Execution**: Immediately evicts Redis edge routing keys (`tenant:slug:...`, `tenant:host:...`).

#### 5.6 Store Theme & Modular Section Customization
- **Method / Path**: `PUT /api/v1/seller/store/theme`
- **Request Body**: `primaryColor`, `secondaryColor`, `fontFamily`, `borderRadius`, `logoUrl`, `layoutConfigJson`.

#### 5.7 Promo Coupon Creation
- **Method / Path**: `POST /api/v1/seller/coupons`
- **Request Body**: `code`, `discountType` (1=%, 2=Flat), `discountValue`, `minOrderAmount`, `maxDiscountCap`, `daysValid`.

#### 5.8 1-Click Courier Shipment Booking & AWB Generation
- **Method / Path**: `POST /api/v1/seller/orders/{orderId}/ship`
- **Execution**: Computes total package dead weight vs. volumetric weight ($\max(\text{Dead}, (L \times W \times H)/5000)$), books courier, generates AWB, and updates order to `ReadyToShip`.

#### 5.9 Download 4x6 Thermal Barcode Shipping Label PDF
- **Method / Path**: `GET /api/v1/seller/orders/{orderId}/shipping-label`
- **Response**: `application/pdf` (4x6 inch / 288x432 pt thermal vector label).

#### 5.10 Download Courier Handover Manifest PDF
- **Method / Path**: `GET /api/v1/seller/shipments/manifest`
- **Response**: `application/pdf` (A4 table of ready-to-ship packages with courier signature lines).

#### 5.11 Business, GST & Inventory Analytics Dashboard
- **Method / Path**: `GET /api/v1/seller/analytics/dashboard`
- **Response**: `HTTP 200 OK` (Gross Merchandise Value, net taxable sales, GST collected, top 5 SKUs by volume, and low-stock SKU alerts).

---

### Module 6: Webhooks & Governance
Base Paths: `/api/v1/webhooks`, `/api/v1/superadmin`

#### 6.1 Courier Live Tracking Webhook Receiver
- **Method / Path**: `POST /api/v1/webhooks/courier/tracking`
- **Auth**: Public (Webhook signature verified in production)
- **Request Body**:
```json
{
  "awbNumber": "AWB261111270333",
  "newStatus": 4,
  "currentLocation": "Janakpuri Delivery Hub, New Delhi",
  "remarks": "Out for delivery with rider Rajesh"
}
```
- **Execution**: Appends checkpoint to `Shipment.Checkpoints` JSONB. Automatically transitions parent `Order.OrderStatus` (`PickedUp` -> `Shipped`, `OutForDelivery` -> `OutForDelivery`, `Delivered` -> `Delivered`).

#### 6.2 Submit Customer Review
- **Method / Path**: `POST /api/v1/buyer/reviews`
- **Execution**: Validates against `Orders` table. Sets `isVerifiedPurchase = true`. Recalculates `AverageRating` and `ReviewCount` on `Product`. Purges Redis PDP cache.

#### 6.3 Super-Admin Trust Badge Moderation
- **Method / Path**: `PATCH /api/v1/superadmin/tenants/{tenantId}/products/{productId}/trust-badge`
- **Request Body**: `{"isAssuredQualityBadge": true, "status": 1}`

#### 6.4 Super-Admin Platform SaaS Overview
- **Method / Path**: `GET /api/v1/superadmin/analytics/overview`
- **Response**: Cross-tenant aggregation of total active merchants, platform GMV, and total order volume.

---

## 5. Seed Test Data & Audit Trail

| Entity | Parameter | Seeded Value |
| :--- | :--- | :--- |
| **Tenant** | Store Name | Urban Threads |
| | Tenant ID | `b00ddadd-cf11-48d4-a90e-394add359357` |
| | Subdomain | `urbanthreads` |
| | Custom Domain | `shop.urbanthreads.in` |
| | State Code | `07` (Delhi) |
| **Seller** | Email | `owner@urbanthreads.in` |
| | Password | `StrongPassword#2026` |
| **Buyer** | Email | `customer@gmail.com` |
| | Password | `BuyerPassword#2026` |
| | Address ID | `3a2af4c3-a443-4eff-85a3-cf4b8b014344` (PIN: `110058`, State: `07`) |
| **Product** | Title | Heavyweight 240GSM Oversized Graphic Tee - Cyber Punk Edition |
| | Product ID | `c619dbf6-14aa-4c2c-95fb-2cae3a011126` |
| | Slug | `heavyweight-240gsm-oversized-graphic-tee---cyber-punk-edition` |
| | Variant (L) | `UT-CYBER-BLK-L` (ID: `c5453679-382b-4ee4-af1b-4d0f33b5ae3e`, ₹999) |
| | Variant (XL)| `UT-CYBER-BLK-XL` (ID: `a5fadf41-e163-480a-a798-78683c1a5b2c`, ₹999) |
| **Orders** | Order ID | `483402e7-d8f2-456a-a430-23f3a432dc44` |
| | Order Number | `ORD-20261108-6575` |
| | Invoice Number | `INV-2026-6575` |
| | AWB Number | `AWB261111270333` |

---

## 6. Frontend Client Integration Checklist (Angular 18)

When developing the frontend applications, enforce the following patterns:
1. **Multi-Tenant HTTP Interceptor**: Injects `X-Custom-Tenant-Domain` automatically during local development, or relies on `window.location.hostname` in production.
2. **Auth Token Storage**: Store JWT in memory or secure storage. Listen for HTTP 401 responses to handle forced multi-device session invalidation.
3. **Zero-Egress Image Uploading**:
   - Call `POST /api/v1/seller/media/presigned-url` to obtain `uploadPutUrl` and `publicCdnUrl`.
   - Execute an HTTP `PUT` directly to `uploadPutUrl` with the raw WebP binary blob.
   - Send `publicCdnUrl` and `objectKey` in the catalog creation payload.
4. **GST Display**: Prices on storefront listings are GST-inclusive. The cart and checkout displays subtotal, coupon savings, and itemized tax breakdown (CGST/SGST for intra-state or IGST for inter-state).