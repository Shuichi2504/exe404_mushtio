using IoTAgriculture.Data;
using IoTAgriculture.Serialization;
using IoTAgriculture.Services;
using IoTAgriculture.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// EventLog can throw AccessDenied during host startup on restricted Windows
// accounts. Console logging works consistently for local runs, containers and
// App Service, and keeps hosted-service failures visible.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddHttpClient<IGeminiService, GeminiService>();
builder.Services.AddControllers().AddJsonOptions(options =>
{
    // SQL Server datetime2 does not retain DateTime.Kind. All persisted app
    // timestamps are UTC, so always expose DateTime values with an explicit Z.
    options.JsonSerializerOptions.Converters.Add(new UtcDateTimeJsonConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<IoTDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        options.UseSqlServer(connectionString);
    }
});

builder.Services.AddHttpClient<IFirebaseRtdbService, FirebaseRtdbService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<ILogbookService, LogbookService>();
builder.Services.AddSingleton<AutomationEngineHealth>();
builder.Services.AddSingleton<DatabaseInitializationHealth>();
builder.Services.AddHostedService<DatabaseInitializationBackgroundService>();
builder.Services.AddHostedService<PumpScheduleBackgroundService>();
builder.Services.AddHostedService<LogbookAutoGenerateBackgroundService>();
builder.Services.AddSingleton<IFirebasePushNotificationService, FirebasePushNotificationService>();

builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration["Cors:AllowedOrigins"];
    var origins = (allowedOrigins ?? string.Empty)
        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    options.AddPolicy(
        "FrontendCors",
        policy =>
        {
            if (origins.Length == 0)
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                return;
            }

            policy.WithOrigins(origins).AllowAnyMethod().AllowAnyHeader();
        });
});

var app = builder.Build();
app.UseSwagger();
    app.UseSwaggerUI();

app.UseCors("FrontendCors");
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/health", (
    AutomationEngineHealth automationHealth,
    DatabaseInitializationHealth databaseHealth) => Results.Ok(new
{
    status = "healthy",
    app = "IoTAgriculture",
    timestamp = DateTimeOffset.UtcNow,
    automation = automationHealth.Snapshot(),
    database = databaseHealth.Snapshot()
}));
app.MapControllers();
app.MapFallbackToFile("index.html");

app.Run();
