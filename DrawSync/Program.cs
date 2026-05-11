using Appwrite;
using Appwrite.Services;
using DrawSync.Models;
using DrawSync.Repositories.Interface;
using DrawSync.UnitOfWork.Interface;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Configure Appwrite Client
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var client = new Client();
    client
        .SetEndpoint(config["Appwrite:Endpoint"]!)
        .SetProject(config["Appwrite:Project"]!);
    return client;
});

// Register Appwrite Services
builder.Services.AddScoped(sp => new TablesDB(sp.GetRequiredService<Client>()));
builder.Services.AddScoped(sp => new Account(sp.GetRequiredService<Client>()));

// Register Repository Layer
builder.Services.AddScoped<IUserRepository, DrawSync.Repositories.Application.UserRepository>();

// Register Unit of Work
builder.Services.AddScoped<DrawSync.UnitOfWork.Interface.IUnitOfWork, DrawSync.UnitOfWork.Application.UnitOfWork>();

// Configure Authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Auth/AccessDenied";
        options.Cookie.Name = "DrawSync.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Strict;
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
