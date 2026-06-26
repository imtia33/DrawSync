using Appwrite;
using Appwrite.Services;
using DrawSync.Hubs;
using DrawSync.Models;
using DrawSync.Repositories.Interface;
using DrawSync.Services;
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

// Register the user-scoped access service (session-based team/db listing + role checks)
builder.Services.AddScoped<IOrgAccessService, OrgAccessService>();

// Register SignalR
builder.Services.AddSignalR();

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

        // For API routes (under /api/), return a clean 403 JSON instead of redirecting
        // to the login page. This lets the frontend's fetch() handlers branch on
        // res.status === 403 cleanly, and keeps the redirect-based UX for MVC pages.
        options.Events.OnRedirectToAccessDenied = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("{\"error\":\"You do not have access to this organization.\"}");
            }
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToLogin = ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/api"))
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                ctx.Response.ContentType = "application/json";
                return ctx.Response.WriteAsync("{\"error\":\"Authentication required.\"}");
            }
            return Task.CompletedTask;
        };
    });

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Only enforce HTTPS redirect outside Development. In Development we run plain HTTP
// behind the Caddy gateway, and UseHttpsRedirection with no HTTPS port configured
// can crash the process on the first request.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map SignalR Hubs
app.MapHub<DrawingHub>("/hubs/drawing");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
