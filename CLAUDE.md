# CLAUDE.md

This file gives Claude Code (claude.ai/code) guidance for working in this repository.

## Overview

Bulky is an ASP.NET Core MVC (.NET 10) online bookstore. It has a customer storefront with a cart and Stripe checkout, an admin portal (categories, products, companies, orders, user registration), and ASP.NET Core Identity with roles. Data lives in **SQL Server or MariaDB**: the `DatabaseProvider` setting picks one per machine. Data access uses EF Core 9 (the MariaDB provider is Pomelo).

## Commands

Run all commands from the repo root (the folder that holds `Bulky.sln`).

```bash
dotnet build Bulky.sln
dotnet run --project BulkyWeb                     # http://localhost:5095 (profile "http")
dotnet run --project BulkyWeb --launch-profile https   # https://localhost:7197
dotnet test Bulky.sln                             # run all unit tests
dotnet test Bulky.Tests --filter "FullyQualifiedName~CartControllerTests"   # one test class
```

**CI:** `.github/workflows/ci.yml` (GitHub Actions) runs on every push to `master` and every pull request. Jobs: `build-and-test` (required by the ruleset), `docker` and `docker-prebuilt`.
- It runs on **ubuntu-latest**: restore, Release build, all tests (TRX results plus Cobertura coverage, uploaded as the `test-results` artifact), then a vulnerable-package check that fails the run.
- Because it runs on Linux, file paths in code and tests must use the exact folder casing.
- Keep the solution building on Linux.

**Docker:** `Dockerfile` (multi-stage: sdk:10.0 to build, aspnet:10.0 to run, as the non-root `$APP_UID`, port 8080) and `docker-compose.yml` (web + mariadb:11.4).
- Secrets come from `.env` (git-ignored, template in `.env.example`), passed as environment variables.
- Named volumes hold `/var/lib/mysql`, `/app/wwwroot/Images/Product` (uploads) and `/app/keys` (data protection keys, through `DataProtection:KeysPath`). Without the keys volume, every new container invalidates logins and anti-forgery tokens.
- `GET /health` checks the database (`HealthChecks/DatabaseHealthCheck`).
- When adding a project that BulkyWeb references, also add its .csproj to the restore layer in the Dockerfile.
- **Prebuilt variant** for networks where containers cannot reach NuGet (the user's office VPN on the router made `dotnet restore` time out inside Docker; custom DNS did not help):
  - `Dockerfile.prebuilt` copies a host `dotnet publish` output (`publish/`, git-ignored), and `docker-compose.prebuilt.yml` overrides the web build. `scripts/docker-prebuilt.cmd` runs both.
  - Keep its runtime setup in sync with the last stage of `Dockerfile`.
- Docker is not installed on the Windows Server dev machine. The CI jobs `docker` and `docker-prebuilt` build and smoke-test both variants.

`dotnet ef` is a local tool pinned in `dotnet-tools.json` (9.0.20, matching EF Core). On a new machine, run `dotnet tool restore` once.

EF Core migrations: each provider has its own migrations project, and the startup project is `BulkyWeb`. **Every model change needs a migration for both providers.** Use the script, which adds both:

```bash
pwsh scripts/add-migration.ps1 <Name>
# or by hand, once per provider ("--" passes the setting to the app at design time):
dotnet ef migrations add <Name> --project Bulky.Migrations.SqlServer --startup-project BulkyWeb -- --DatabaseProvider SqlServer
dotnet ef migrations add <Name> --project Bulky.Migrations.MariaDb   --startup-project BulkyWeb -- --DatabaseProvider MariaDb
dotnet ef migrations has-pending-model-changes --project Bulky.Migrations.<Provider> --startup-project BulkyWeb -- --DatabaseProvider <Provider>
```

`DbInitializer` applies pending migrations at startup, so `database update` is rarely needed.

**NuGet sources:** the repo `NuGet.Config` clears inherited package sources and source mappings, and maps every package (`*`) to nuget.org. Builds then do not depend on machine or user NuGet settings: a user-level `packageSourceMapping` caused NU1100 on the user's office PC. If a package must ever come from another feed, add that feed and its pattern there.

**EF Core must stay on 9.0.x** in every project (including `Microsoft.EntityFrameworkCore.SqlServer` and `Microsoft.EntityFrameworkCore.InMemory` in the tests) until Pomelo releases a version for EF Core 10. Pomelo 9 only accepts `[9.0.0, 9.0.999]`. For the same reason, `Microsoft.VisualStudio.Web.CodeGeneration.Design` is not referenced: version 10 requires EF Core 10.

- `Bulky.Migrations.SqlServer` holds the original SQL Server history: 14 migrations from 2025, plus `RemoveDiscriminatorFromUsers`. Never delete or edit applied migrations, because existing SQL Server databases depend on them.
- `Bulky.Migrations.MariaDb` starts from its own `InitialCreate`.
## Solution layout and dependency direction

`BulkyWeb` → `Bulky.DataAccess` → `Bulky.Models` → `Bulky.Utility`

`BulkyWeb` also references `Bulky.Migrations.SqlServer` and `Bulky.Migrations.MariaDb`, which both reference `Bulky.DataAccess`. EF loads the matching one through `MigrationsAssembly(...)`. `Bulky.Tests` references the app projects.

- **Bulky.Utility**: `SD` holds the static constants: role names, order and payment status strings, and the `SessionCart` session key. Always use these constants and never hard-code the strings. The project also contains `StripeSettings` (bound from the `Stripe` config section) and `EmailSender` / `IdentityUiEmailSenderAdapter`, which are no-op email senders used by Identity.
- **Bulky.Models**: EF entities (`Category`, `Product`, `Company`, `ShoppingCart`, `OrderHeader`, `OrderDetail`, `ApplicationUser : IdentityUser`) and `ViewModels/` (`ProductVM`, `ShoppingCartVM`, `OrderVM`).
- **Bulky.DataAccess**:
  - `Data/ApplicationDbContext`: `IdentityDbContext<ApplicationUser>`. Seed data for categories, companies and products goes in `OnModelCreating` through `HasData`.
  - `Data/DatabaseServiceCollectionExtensions.cs`: `AddBulkyDatabase(configuration)` reads `DatabaseProvider` (`SqlServer` by default, or `MariaDb`). It registers the context with that provider and its migrations assembly. Keep the model provider-neutral: no `HasColumnType("nvarchar(...)")` or other provider-specific SQL.
  - `Repository/`: a generic `Repository<T>` plus one repository per entity, all exposed through `IUnitOfWork`.
  - `DBInitializer/DbInitializer`: runs at startup.
    - It applies pending migrations (relational providers only), creates any missing roles, and creates the first admin from the `AdminUser` settings when no user is in the Admin role.
    - Every step is idempotent, and Identity errors (`IdentityResult`) are turned into exceptions.
    - It never promotes an existing account.
- **BulkyWeb**: the MVC app. `Program.cs` holds all DI, Identity, session and Stripe setup.
- **Bulky.Tests**: xUnit tests.
  - Controller tests run the real controllers and the real `UnitOfWork` on the EF Core InMemory provider. Create the context with `TestDb.Create()`, and call `db.SaveAndDetach()` after seeding.
  - `CategoryControllerTests` shows the Moq style: a mocked `IUnitOfWork` and `Verify(...)`.
  - `controller.WithUser(userId, roles...)` fakes the logged-in user, the session and TempData.
  - Model validation attributes do not run in unit tests. Only errors the controller adds itself appear in `ModelState`.

## Key patterns and conventions

- **Repository + Unit of Work.** Controllers inject `IUnitOfWork`. Never inject `ApplicationDbContext` into controllers. Call `_unitOfWork.Save()` to commit.
  - `GetAll(filter, includeProperties)` / `Get(filter, includeProperties, tracked)`. `includeProperties` is a comma-separated string of navigation names, e.g. `"Category"` or `"Product,ApplicationUser"`.
  - `Get` uses `AsNoTracking()` by default. Pass `tracked: true` when you will modify the returned entity and rely on change tracking.
  - The generic repository deliberately has **no `Update`**. Each entity repository defines its own `Update`. Some map fields by hand, for example `ProductRepository.Update`, which only overwrites `ImageURL` when a new one is supplied.
  - Adding a new entity means: model in `Bulky.Models` → `DbSet` in `ApplicationDbContext` → `I<Entity>Repository` + `<Entity>Repository` → property on `IUnitOfWork`/`UnitOfWork` → migration.
- **Areas.** `Customer` is the default area (route `{area=Customer}/{controller=Home}/{action=Index}/{id?}`), `Admin` is the back office, and `Identity` holds the scaffolded Identity Razor Pages (customized, e.g. `Register` handles roles and company). Every controller needs `[Area("...")]`.
- **Authorization.** Use `[Authorize(Roles = SD.Role_Admin)]` or combine roles, e.g. `SD.Role_Admin + "," + SD.Role_Employee`. The roles are Customer, Company, Admin and Employee.
- **Resource ownership (IDOR).** Never load a record only by an id from the URL or form.
  - Put the owner in the query: `Get(a => a.Id == id && a.ApplicationUserId == User.GetUserId())`, and return `NotFound()` when it is null.
  - For orders, use `OrderController.GetOrderForCurrentUser`: staff (`User.IsStaff()`) may open any order.
  - The `GetUserId()` / `IsStaff()` extensions live in `Bulky.Utility/ClaimsPrincipalExtensions.cs`.
- **CSRF.** A global `AutoValidateAntiforgeryTokenAttribute` checks the token on every non-GET request.
  - Actions that change data must be POST, PUT or DELETE, never GET.
  - Use `<button type="submit" asp-action=...>` inside a `method="post"` form, not `<a>` links.
  - jQuery AJAX sends the token automatically: `site.js` reads it from the `csrf-token` meta tag in `_Layout`.
  - An endpoint called by an external server (e.g. a future Stripe webhook) needs `[IgnoreAntiforgeryToken]`.
- **Admin list pages** use DataTables, loaded by AJAX from `wwwroot/js/{product,company,order}.js`. Controllers expose JSON endpoints in a `#region API CALLS` block: `GetAll` returns `Json(new { data = ... })`, and `[HttpDelete] Delete` returns `{ success, message }`. Deletes are confirmed with SweetAlert2.
  - Return only the columns the table needs, projected with `Select(o => new { ... })` (see `OrderController.GetAll`).
  - Never serialize entities that include `ApplicationUser`, because that sends `PasswordHash`, `SecurityStamp`, etc. to the browser.
  - Order tabs: `pending` filters `PaymentStatus == SD.PaymentStatusDelayedPayment`. `inprocess`, `completed` and `approved` filter `OrderStatus`.
- **Notifications.** Set `TempData["success"]` / `TempData["error"]`. `Views/Shared/_Notification.cshtml` renders them with Toastr.
- **Upsert.** Create and edit share one `Upsert(int? id)` action and view (Product, Company).
- **Product images**
  - Uploads are saved to `wwwroot/Images/Product/` with GUID file names, and `ImageURL` is stored as `/Images/Product/<file>`. Use forward slashes and that exact casing, because Linux is case-sensitive. Older rows may still hold `\images\product\...`.
  - The folder is git-ignored and is created on upload with `Directory.CreateDirectory`.
  - Old files are deleted on replace or delete, but only through `ProductController.GetImageFilePath`. On edit the old URL comes from a hidden form field, so that method returns null for any path outside `wwwroot/Images/Product`.
  - Views render covers with `Url.ProductImage(product.ImageURL)` (`BulkyWeb/Extensions/UrlHelperExtensions.cs`). It falls back to `~/Images/book.png` when `ImageURL` is empty, as it is for every seeded product.
- **Never save a model-bound object directly (over-posting).** Model binding fills every posted property, not only the fields in the form.
  - Build the entity from the fields that may come from the form. See `CartController.SummaryPOST` (only the 6 shipping fields) and `HomeController.Details` POST (only ProductId and Count).
  - Re-check validation rules on the server. `[Range]` and `[Required]` only stop honest browsers, and unit tests do not run them.
- **Save related rows in one `_unitOfWork.Save()`.** One SaveChanges runs in a single transaction. Link children through the navigation property (`OrderDetail.OrderHeader = header`) instead of saving the parent first to get its id.
  - Never keep a transaction open while calling an external service (Stripe). Save first, then call out.
- **Deletes never remove order history.**
  - Products are **soft-deleted** (`Product.IsDeleted`). The admin `Delete` sets the flag and removes the product from all carts.
  - Every query for the store or the admin list must filter `!p.IsDeleted` explicitly. Order queries must **not** filter, so old orders still show what was bought.
  - Do not use a global `HasQueryFilter` for this. `OrderDetail.Product` is required, so a filter would silently drop order lines.
  - `OrderDetail → Product` and `Product → Category` are `DeleteBehavior.Restrict`.
  - Deleting a category with products, or a company with users, is refused with a message (TempData error, or `{ success: false, message }` for AJAX, which product.js/company.js show as an error toast).
- **Cart count** is cached in session under `SD.SessionCart` and rendered by `ViewComponents/ShoppingCartViewComponent`. Any code that adds or removes cart items must update the session value, as `HomeController.Details` (POST) and `CartController.Minus/Remove` do.
- **Money is always `decimal`, never `double`.**
  - `ApplicationDbContext.ConfigureConventions` stores every `decimal` as `decimal(18,2)`.
  - Decimal literals need the `m` suffix (`5.5m`).
  - Convert amounts for Stripe only with `Money.ToStripeAmount(amount)` (`Bulky.Utility/Money.cs`), which rounds to whole cents.
  - When a migration changes seeded columns, EF may add `UpdateData` calls that overwrite seeded rows. Remove them if they would reset data an admin may have edited (see `MoneyAsDecimal`).
- **Pricing tiers.** `Product` has `Price`, `Price50` and `Price100`. `CartController.GetPriceBasedOnQuantity` picks one based on quantity.
- **Orders / Stripe.** Regular customers pay immediately through a Stripe Checkout session (`CartController.SummaryPOST` → `OrderConfirmation`). Company users get delayed payment (`SD.PaymentStatusDelayedPayment`) and pay later from `Admin/Order/Details` (`DetailsPayNow` → `PaymentConfirmation`). Order status transitions go through `OrderHeaderRepository.UpdateStatus` / `UpdateStripePaymentId`. Cancelling a paid order issues a Stripe refund.
  - Stripe Success/Cancel URLs must never be hard-coded. Build them with `Url.Action(..., protocol: Request.Scheme)` (see `CartController.GetOrderConfirmationUrl` / `OrderController.GetPaymentConfirmationUrl`), so they follow the current domain and port.
  - **Webhook:** `Controllers/StripeWebhookController` (`POST /stripe/webhook`) has `[AllowAnonymous]` and `[IgnoreAntiforgeryToken]`. It verifies the `Stripe-Signature` header with `Stripe:WebhookSecret`, and handles `checkout.session.completed` / `async_payment_succeeded`. It always answers 200 to genuine events, otherwise Stripe retries.
  - Record payments only with `OrderHeaderRepository.MarkPaid`. It is idempotent, because the confirmation pages and the webhook may both report the same payment, and it keeps the order status of delayed (company) orders.
  - Test webhook payloads with `Bulky.Tests/Helpers/StripeWebhookSigner.cs`. Real Stripe events always contain a `request` object, and Stripe.net fails to parse events without it.
  - `StripeReturnUrlTests` checks these URLs against the real route table (`Bulky.Tests/Helpers/RealUrlHelper.cs`).
  - When deploying, set `AllowedHosts` to the real domain, because the URLs use the request's Host header.
- The front end uses Bootstrap (the site CSS is based on a Bootswatch theme, and reference files are in `BulkyWeb/Documents/`), Bootstrap Icons, jQuery, Toastr, SweetAlert2, DataTables and TinyMCE. Everything except jQuery/Bootstrap comes from CDNs in `_Layout.cshtml`. TinyMCE is the exception: it is loaded only in `Admin/Views/Product/Upsert.cshtml`.
- **TinyMCE** is set up in `wwwroot/js/tinymce-setup.js`. List only **free** plugins and toolbar buttons there. The Tiny Cloud key has no premium plan, and every premium plugin shows an "is not enabled on your API key" warning in the editor.

## Configuration

- `appsettings.json` has `"DatabaseProvider": "SqlServer"` and one connection string per provider, named after it.
  - `ConnectionStrings:SqlServer` uses `Server=.` with Windows authentication, so a SQL Server machine needs no extra setup.
  - `ConnectionStrings:MariaDb` has no real password (`SET_IN_USER_SECRETS`).
- A MariaDB machine overrides two values in user secrets (`UserSecretsId` in `BulkyWeb.csproj`):
  `dotnet user-secrets set "DatabaseProvider" "MariaDb" --project BulkyWeb`
  `dotnet user-secrets set "ConnectionStrings:MariaDb" "Server=localhost;Port=3306;Database=Bulky;User=bulky;Password=..." --project BulkyWeb`
- Create the MariaDB account once per machine with `scripts/create-mariadb-user.sql`, run as root.
- The MariaDB server version is set explicitly in `AddBulkyDatabase` (`MariaDbServerVersion(11, 4)`) rather than auto-detected, so `dotnet ef` works without a database connection.
- The `Stripe:SecretKey` / `Stripe:PublishableKey` and `SendGrid:SecretKey` values are blank in the committed config. Supply them through user secrets or environment variables, and never commit real keys.
- The database is migrated automatically at startup by `DbInitializer`, so a fresh DB is created on first run.
- **The first admin comes from configuration, never from code.**
  - `AdminUser:Email` is in `appsettings.json` (`admin@bulky.com`). `AdminUser:Password` is empty there and must be set before the first start on an empty database:
    `dotnet user-secrets set "AdminUser:Password" "..." --project BulkyWeb`, or the `AdminUser__Password` environment variable.
  - Without it, startup fails with that hint. Databases that already have an admin need no password setting.
