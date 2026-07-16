using IoTAgriculture.Data;
using IoTAgriculture.Services;
using IoTAgriculture.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Missing connection string 'ConnectionStrings:DefaultConnection'. " +
        "Set it in appsettings.json, appsettings.Development.json, or an environment variable.");
}

builder.Services.AddHttpClient<GeminiService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<IoTDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddHttpClient<IFirebaseRtdbService, FirebaseRtdbService>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IAlertService, AlertService>();
builder.Services.AddScoped<ILogbookService, LogbookService>();
builder.Services.AddHostedService<PumpScheduleBackgroundService>();
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
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    app = "MUSHTIO1",
    timestamp = DateTimeOffset.UtcNow
}));
app.MapControllers();
app.MapFallbackToFile("index.html");

await AuthSchemaInitializer.InitializeAsync(app.Services);

app.Run();
