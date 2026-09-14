global using Newtonsoft.Json;
global using static LeagueSitesBackend.Models.Extensions;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("Sqlite");
if (!string.IsNullOrEmpty(connectionString))
{
    builder.Services.AddDbContext<LeagueSitesContext>(options =>
    {
        options.UseSqlite(connectionString);
    });
}

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
    });

builder.Services.AddScoped<IPermissionsService, PermissionsService>();
builder.Services.AddScoped<ISeasonService, SeasonService>();
builder.Services.AddScoped<IScheduleImportService, ScheduleImportService>();
builder.Services.AddScoped<ITournamentService, TournamentService>();
builder.Services.AddScoped<ITournamentSeasonService, TournamentSeasonService>();
builder.Services.AddScoped<IPublicTournamentService, PublicTournamentService>();
builder.Services.AddLeagueSitesAuthorization();

builder.Services.AddControllers().AddJsonOptions(o =>
                o.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles);

builder.Services.AddHttpClient();

builder.Services.Configure<ForwardedHeadersOptions>(options => 
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.WebHost.ConfigureKestrel((context, serverOptions) =>
{
    var kestrelSection = context.Configuration.GetSection("Kestrel");

    serverOptions.Configure(kestrelSection);
});

var app = builder.Build();

// Bring this tenant's database up to the current schema before serving
// anything. Throws (and prevents startup) if a migration fails — a backend
// with a broken schema must not serve traffic.
if (!string.IsNullOrEmpty(connectionString))
{
    DatabaseMigrator.Migrate(
        connectionString,
        app.Services.GetRequiredService<ILogger<Program>>());
}

// Exception logging — must be early in pipeline to catch all downstream errors
app.UseMiddleware<ExceptionLoggingMiddleware>();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    //app.UseHsts();
}

//app.UseHttpsRedirection();
app.UseStaticFiles();

// Serve tenant-specific static assets from the persistent volume at
// /var/db/static/. In production Apache aliases handle these paths and
// requests never reach Kestrel; in dev (no Apache) this lets the
// backend serve them directly so URLs like /images/social/Facebook.webp
// and /favicon.png work. Mapped at the root so anything under the
// volume (images/, files/, favicon.png) is reachable.
var staticVolume = "/var/db/static";
if (Directory.Exists(staticVolume))
{
    app.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(staticVolume),
        RequestPath = ""
    });
}

app.UseCookiePolicy();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
