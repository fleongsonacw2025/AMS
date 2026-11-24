using AMS;
using AMS.Data;
using AMS.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();

// 1. Register Auth & Protected Storage Services
builder.Services.AddAuthenticationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorizationCore();
builder.Services.AddScoped<ProtectedSessionStorage>();

// 2. Register Custom AuthenticationStateProvider (and expose concrete type for direct updates)
builder.Services.AddScoped<SimpleAuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<SimpleAuthStateProvider>());

// 3. Register Database Context & Application Services
builder.Services.AddDbContext<AmsDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=ams.db"));

builder.Services.AddScoped<AttendanceService>();

var app = builder.Build();

// Apply pending EF Core migrations on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AmsDbContext>();
    db.Database.Migrate();
}

app.UseStaticFiles();
app.UseRouting();

// 4. Add Auth Middleware between UseRouting and MapBlazorHub
app.UseAuthentication();
app.UseAuthorization();

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();