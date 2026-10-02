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
- **Notifications.** Set `TempData["success"]` / `TempData["error"]`. `Views/Shared/_Notification.cshtml` renders them with Toastr.
- **Upsert.** Create and edit share one `Upsert(int? id)` action and view (Product, Company).
- **Product images**
  - Uploads are saved to `wwwroot/Images/Product/` with GUID file names, and `ImageURL` is stored as `/Images/Product/<file>`. Use forward slashes and that exact casing, because Linux is case-sensitive. Older rows may still hold `\images\product\...`.
  - The folder is git-ignored and is created on upload with `Directory.CreateDirectory`.
  - Old files are deleted on replace or delete, but only through `ProductController.GetImageFilePath`. On edit the old URL comes from a hidden form field, so that method returns null for any path outside `wwwroot/Images/Product`.
  - Views render covers with `Url.ProductImage(product.ImageURL)` (`BulkyWeb/Extensions/UrlHelperExtensions.cs`). It falls back to `~/Images/book.png` when `ImageURL` is empty, as it is for every seeded product.
- **Cart count** is cached in session under `SD.SessionCart` and rendered by `ViewComponents/ShoppingCartViewComponent`. Any code that adds or removes cart items must update the session value, as `HomeController.Details` (POST) and `CartController.Minus/Remove` do.
- **Pricing tiers.** `Product` has `Price`, `Price50` and `Price100`. `CartController.GetPriceBasedOnQuantity` picks one based on quantity.
- **Orders / Stripe.** Regular customers pay immediately through a Stripe Checkout session (`CartController.SummaryPOST` → `OrderConfirmation`). Company users get delayed payment (`SD.PaymentStatusDelayedPayment`) and pay later from `Admin/Order/Details` (`DetailsPayNow` → `PaymentConfirmation`). Order status transitions go through `OrderHeaderRepository.UpdateStatus` / `UpdateStripePaymentId`. Cancelling a paid order issues a Stripe refund.
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
