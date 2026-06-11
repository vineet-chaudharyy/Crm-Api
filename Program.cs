using System.Text;
using Crm_Api.Application.Dtos;
using Crm_Api.Application.Interfaces;
using Crm_Api.Application.Services;
using Crm_Api.Infrastructure.Auth;
using Crm_Api.Infrastructure.GoogleSheets;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration objects -------------------------------------------------
builder.Services.Configure<GoogleSheetsOptions>(
    builder.Configuration.GetSection(GoogleSheetsOptions.SectionName));
builder.Services.Configure<JwtOptions>(
    builder.Configuration.GetSection(JwtOptions.SectionName));

// ---- Infrastructure (Google Sheets = data store) ---------------------------
builder.Services.AddSingleton<GoogleSheetsClient>();
builder.Services.AddScoped<ILeadRepository, LeadRepository>();
builder.Services.AddScoped<IEmployeeRepository, EmployeeRepository>();
builder.Services.AddScoped<IActivityLogRepository, ActivityLogRepository>();
builder.Services.AddScoped<IReminderRepository, ReminderRepository>();
builder.Services.AddScoped<IMetaEventRepository, MetaEventRepository>();
builder.Services.AddScoped<IAttendanceRepository, AttendanceRepository>();

// ---- Application services ---------------------------------------------------
builder.Services.AddScoped<DashboardService>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<DataSeeder>();
// SettingsService must be Singleton — it holds in-memory state + event subscriptions
builder.Services.AddSingleton<SettingsService>();

// ---- Facebook auto-sync -----------------------------------------------------
builder.Services.AddScoped<FacebookSyncService>();
builder.Services.AddHostedService<FacebookSyncBackgroundService>();

// ---- Meta Conversions API ---------------------------------------------------
builder.Services.Configure<MetaConversionOptions>(
    builder.Configuration.GetSection(MetaConversionOptions.SectionName));
builder.Services.AddHttpClient<IMetaConversionService, MetaConversionService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});
// Named client used by SettingsController.TestMeta
builder.Services.AddHttpClient("meta-test", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
});

// ---- AuthN / AuthZ ----------------------------------------------------------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key))
        };
    });
builder.Services.AddAuthorization();

// ---- CORS -------------------------------------------------------------------
const string CorsPolicy = "FrontendCors";
var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
              ?? new[] { "http://localhost:5173" };
builder.Services.AddCors(o => o.AddPolicy(CorsPolicy,
    p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

// ---- MVC + Swagger (with JWT) ----------------------------------------------
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "DIGIHOOK CRM API", Version = "v1" });
    var scheme = new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the JWT token from /api/auth/login (without 'Bearer ').",
        Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
    };
    c.AddSecurityDefinition("Bearer", scheme);
    c.AddSecurityRequirement(new OpenApiSecurityRequirement { { scheme, Array.Empty<string>() } });
});

var app = builder.Build();

// ---- Seed default admin on startup -----------------------------------------
using (var scope = app.Services.CreateScope())
{
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync();
}

// Swagger available in all environments (useful for API testing on Azure)
app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();
app.UseCors(CorsPolicy);

// ---- Security headers — prevent clickjacking, MIME sniffing, data leaks ----
app.Use(async (ctx, next) =>
{
    var h = ctx.Response.Headers;

    // Block embedding in iframes (prevents clickjacking + screen-scraping via hidden iframe)
    h["X-Frame-Options"] = "DENY";

    // Prevent MIME-type sniffing
    h["X-Content-Type-Options"] = "nosniff";

    // Block sending Referer header to external sites
    h["Referrer-Policy"] = "strict-origin-when-cross-origin";

    // Disable caching of API responses so browser history / back-button can't replay lead data
    h["Cache-Control"] = "no-store, no-cache, must-revalidate, private";
    h["Pragma"] = "no-cache";

    // Remove server identity
    h.Remove("Server");
    h.Remove("X-Powered-By");

    // Content-Security-Policy — restrict what the frontend can load
    // Adjust 'connect-src' if your API is on a different host in production
    h["Content-Security-Policy"] =
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
        "font-src 'self' https://fonts.gstatic.com; " +
        "img-src 'self' data:; " +
        "connect-src 'self'; " +
        "frame-ancestors 'none';";   // belt-and-suspenders with X-Frame-Options

    await next();
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
