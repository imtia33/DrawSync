using Appwrite;
using Appwrite.Services;
using DrawSync.Models;
using DrawSync.Repositories.Interface;
using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Load optional local secrets (do not commit appsettings.Local.json)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Prefer user secrets for local API key overrides
builder.Configuration.AddUserSecrets<Program>(optional: true, reloadOnChange: true);

// Configure Appwrite client per-request (Scoped) to ensure thread-safe session isolation
builder.Services.AddScoped(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    
    var client = new Client();
    client
        .SetEndpoint(config["Appwrite:Endpoint"]!)
        .SetProject(config["Appwrite:Project"]!);

    var httpContext = httpContextAccessor.HttpContext;
    var sessionClaim = httpContext?.User?.FindFirst("AppwriteSession")?.Value;
    if (!string.IsNullOrEmpty(sessionClaim))
    {
        client.SetSession(sessionClaim);
    }
    
    return client;
});

// Register Appwrite Services
builder.Services.AddScoped(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var adminClient = new Client();
    adminClient
        .SetEndpoint(config["Appwrite:Endpoint"]!)
        .SetProject(config["Appwrite:Project"]!);

    var apiKey = config["Appwrite:ApiKey"];
    if (!string.IsNullOrWhiteSpace(apiKey))
    {
        adminClient.SetKey(apiKey);
    }

    return new TablesDB(adminClient);
});
builder.Services.AddScoped(sp => new Account(sp.GetRequiredService<Client>()));

// Register Repository Layer
builder.Services.AddScoped<IUserRepository, DrawSync.Repositories.Application.UserRepository>();
builder.Services.AddScoped<DrawSync.Repositories.Interface.IOrganizationRepository, DrawSync.Repositories.Application.OrganizationRepository>();
builder.Services.AddScoped<DrawSync.Repositories.Interface.IDrawingRepository, DrawSync.Repositories.Application.DrawingRepository>();
builder.Services.AddScoped<DrawSync.Repositories.Interface.IInvoiceRepository, DrawSync.Repositories.Application.InvoiceRepository>();
builder.Services.AddScoped<DrawSync.Repositories.Interface.IUsageRepository, DrawSync.Repositories.Application.UsageRepository>();

// Register Unit of Work
builder.Services.AddScoped<DrawSync.UnitOfWork.Interface.IUnitOfWork, DrawSync.UnitOfWork.Application.UnitOfWork>();

// Admin Teams service (uses API key if configured)
builder.Services.AddScoped(sp => {
    var config = sp.GetRequiredService<IConfiguration>();
    var adminClient = new Client();
    adminClient
        .SetEndpoint(config["Appwrite:Endpoint"]!)
        .SetProject(config["Appwrite:Project"]!);
    var apiKey = config["Appwrite:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey)) adminClient.SetKey(apiKey);
    return new Teams(adminClient);
});

// Configure Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.Cookie.Name = "DrawSync.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
        options.SlidingExpiration = true;
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
