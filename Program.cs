using Microsoft.EntityFrameworkCore;
using AMS.Data;
using AMS.Services;
using Microsoft.AspNetCore.Components.Authorization; // Added for Auth

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// 1. Register Auth Services
builder.Services.AddAuthenticationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();

// 2. Register your Custom State Provider (ensure the file exists)
builder.Services.AddScoped<AuthenticationStateProvider, SimpleAuthStateProvider>();

// Register the Database Context
builder.Services.AddDbContext<AmsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=ams.db"));

builder.Services.AddScoped<AttendanceService>();

var app = builder.Build();

// Ensure the database file is created locally[cite: 6]
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AmsDbContext>();
    db.Database.EnsureCreated();
}

app.UseStaticFiles();
app.UseRouting();

// 3. Add Auth Middleware (Must be between UseRouting and MapBlazorHub)
app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();