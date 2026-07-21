using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MySqlConnector;
using System.Text;
using System.Text.Json.Serialization;
using FinanceApp.API.Services;
using FinanceApp.Data.Data;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins(
                "http://173.249.42.11",
                "http://173.249.42.11:80",
                "http://173.249.42.11:3000",
                "https://173.249.42.11",
                "http://localhost:5173",
                "http://localhost:3000"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "FinanceApp API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Введите JWT токен"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("JWT signing key is not configured.");
var serverVersion = new MariaDbServerVersion(
    string.IsNullOrWhiteSpace(builder.Configuration["Database:MariaDbVersion"])
        ? new Version(10, 5, 23)
        : Version.Parse(builder.Configuration["Database:MariaDbVersion"]!));

var dbConnectionStringBuilder = new MySqlConnectionStringBuilder(connectionString);
if (string.Equals(dbConnectionStringBuilder.Server, "localhost", StringComparison.OrdinalIgnoreCase))
{
    dbConnectionStringBuilder.Server = "127.0.0.1";
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(
        dbConnectionStringBuilder.ConnectionString,
        serverVersion,
        mySqlOptions => mySqlOptions.EnableRetryOnFailure()));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IStockHistoryService, StockHistoryService>();
builder.Services.AddHostedService<StockHistoryRefreshHostedService>();

var app = builder.Build();

app.Logger.LogInformation(
    "Using application content root {ContentRootPath} for backend configuration.",
    app.Environment.ContentRootPath);
app.Logger.LogDebug(
    "Configured MariaDB connection for server {Server} and database {Database}.",
    dbConnectionStringBuilder.Server,
    dbConnectionStringBuilder.Database);

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "FinanceApp API v1");
    c.RoutePrefix = "swagger";
});

// Handle OPTIONS preflight before routing
app.Use(async (context, next) =>
{
    if (context.Request.Method == "OPTIONS")
    {
        var origin = context.Request.Headers["Origin"].ToString();
        var allowedOrigins = new[]
        {
            "http://173.249.42.11",
            "http://173.249.42.11:80",
            "http://173.249.42.11:3000",
            "https://173.249.42.11",
            "http://localhost:5173",
            "http://localhost:3000"
        };
        if (allowedOrigins.Contains(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
            context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
            context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
            context.Response.Headers["Access-Control-Max-Age"] = "86400";
        }
        context.Response.StatusCode = 204;
        return;
    }
    await next();
});

app.UseRouting();
app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
