using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MWFinance.API.Helpers;
using MWFinance.Domain.Interfaces;
using MWFinance.Infrastructure.Data;
using MWFinance.Infrastructure.Repositories;
using System;
using System.Text;
using Serilog;
using Serilog.Sinks.MySQL;               // <-- replaces Serilog.Sinks.PostgreSQL


try
{
    // Enable Serilog's internal diagnostic output so sink errors are visible
    Serilog.Debugging.SelfLog.Enable(msg => Console.WriteLine($"[SERILOG INTERNAL] {msg}"));

    var builder = WebApplication.CreateBuilder(args);


    Console.WriteLine("=== MWFinance API Starting ===");
    Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");

    // DIAGNOSTIC: Print config values so you can verify them
    Console.WriteLine($"DB Connection: {(string.IsNullOrEmpty(builder.Configuration.GetConnectionString("DefaultConnection")) ? "MISSING!" : "Found")}");
    Console.WriteLine($"Jwt:Key:      {(string.IsNullOrEmpty(builder.Configuration["Jwt:Key"]) ? "MISSING!" : "Found (length=" + builder.Configuration["Jwt:Key"]!.Length + ")")}");
    Console.WriteLine($"Jwt:Issuer:   {builder.Configuration["Jwt:Issuer"] ?? "MISSING!"}");
    Console.WriteLine($"Jwt:Audience: {builder.Configuration["Jwt:Audience"] ?? "MISSING!"}");

    // Get the connection string — same one your API already uses
    string logConnectionString = builder.Configuration
        .GetConnectionString("DefaultConnection")!;

    Log.Logger = new LoggerConfiguration()
        .MinimumLevel.Debug()
        .Enrich.FromLogContext()
        .WriteTo.Console()
        .WriteTo.File(
            path: "C:/MWFinanceLogs/log-.txt",
            rollingInterval: RollingInterval.Day,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
        // Serilog.Sinks.MySQL: table is auto-created on first write.
        // NuGet: Install-Package Serilog.Sinks.MySQL
        .WriteTo.MySQL(
            connectionString: logConnectionString,
            tableName: "MwfDDAMarketPlaceLogs",
            storeTimestampInUtc: true)
        .CreateLogger();
    builder.Host.UseSerilog();

    // Add services to the container.

    builder.Services.AddControllers();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "MWFinance API",
            Version = "v1",
            Description = "Direct Debit middleware API for Fintech integrations"
        });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT token like this: Bearer {your token here}"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
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

    // ── MySQL via Pomelo ────────────────────────────────────────────────
    // NuGet: Install-Package Pomelo.EntityFrameworkCore.MySql
    // Remove Npgsql.EntityFrameworkCore.PostgreSQL package reference.
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    builder.Services.AddDbContext<ApplicationDbContext>(options =>
        options.UseMySql(
            connectionString,
            ServerVersion.AutoDetect(connectionString),
            mySqlOptions => mySqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null)));

    builder.Services.Configure<DdaGatewaySettingsHelper>(
    builder.Configuration.GetSection(DdaGatewaySettingsHelper.SectionName));


    builder.Services.AddHttpClient();
    builder.Services.AddScoped<IDdaRepository, DdaRepository>();
    builder.Services.AddEndpointsApiExplorer();
    // NOTE: AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true) removed —
    // this was a Postgres/Npgsql-only compatibility switch and has no MySQL equivalent/need.

    var jwtKey = builder.Configuration["Jwt:Key"]
        ?? throw new InvalidOperationException("Jwt:Key is missing from appsettings.json");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,   // rejects expired tokens
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidAudience = builder.Configuration["Jwt:Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ClockSkew = TimeSpan.Zero  // no grace period on expiry
            };
        });
    builder.Services.AddAuthorization();

    Console.WriteLine("=== App built. Configuring pipeline... ===");

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseMiddleware<MWFinance.API.Middleware.LogEnrichmentMiddleware>();
    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.Run();
    Console.WriteLine("=== Pipeline ready. Starting server... ===");
}
catch (Exception ex)
{
    throw;
}
