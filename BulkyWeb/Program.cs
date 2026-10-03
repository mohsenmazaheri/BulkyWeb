using Bulky.DataAccess.Data;
using Bulky.DataAccess.DBInitializer;
using Bulky.DataAccess.Repository;
using Bulky.DataAccess.Repository.IRepository;
using Bulky.Utility;
using Microsoft.AspNetCore.Identity;
using Bulky.Models;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using BulkyWeb.HealthChecks;
using Stripe;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Validate the anti-forgery token on every unsafe request (POST, PUT, DELETE...) to prevent CSRF.
// GET, HEAD, OPTIONS and TRACE are skipped, so GET actions must never change data.
builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));

// SQL Server or MariaDB, chosen by the "DatabaseProvider" setting (see appsettings.json)
builder.Services.AddBulkyDatabase(builder.Configuration);

// Fetching Stripe settings into StripeSettings properties 
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection("Stripe"));
// The first admin account, created by DbInitializer on an empty database (password from user secrets)
builder.Services.Configure<AdminUserSettings>(builder.Configuration.GetSection(AdminUserSettings.SectionName));

//builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<ApplicationDbContext>();
//builder.Services.AddDefaultIdentity<IdentityUser>().AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.AddIdentity<ApplicationUser, IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = $"/Identity/Account/Login";
    options.LogoutPath = $"/Identity/Account/Logout";
    options.AccessDeniedPath = $"/Identity/Account/AccessDenied";
});

//------------- Adding sessions options ---
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IOTimeout = TimeSpan.FromMinutes(100);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});
//-----------------------------------------
builder.Services.AddScoped<IDbInitializer, DbInitializer>();
builder.Services.AddRazorPages();

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IEmailSender<IdentityUser>, EmailSender>();
builder.Services.AddScoped<IEmailSender, IdentityUiEmailSenderAdapter>();

// GET /health: lets Docker, CI and hosting platforms check that the app runs and reaches its database
builder.Services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

// Login cookies and anti-forgery tokens are encrypted with "data protection" keys. By default the keys live
// inside the container, so every new container would log everyone out. In Docker, the keys go to a volume.
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrEmpty(dataProtectionKeysPath))
{
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath))
        .SetApplicationName("BulkyWeb");
}

var app = builder.Build();

// Configure the HTTP request pipeline.
//if (!app.Environment.IsProduction())
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
StripeConfiguration.ApiKey = builder.Configuration.GetSection("Stripe:SecretKey").Get<string>();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.UseSession();
await SeedDatabaseAsync();

app.MapRazorPages();
app.MapHealthChecks("/health");

app.MapControllerRoute(
    name: "default",
    pattern: "{area=Customer}/{controller=Home}/{action=Index}/{id?}");

app.Run();

async Task SeedDatabaseAsync()
{
    using (var scope = app.Services.CreateScope())
    {
        var dbInitializer = scope.ServiceProvider.GetRequiredService<IDbInitializer>();
        await dbInitializer.InitializeAsync();
    }
}
