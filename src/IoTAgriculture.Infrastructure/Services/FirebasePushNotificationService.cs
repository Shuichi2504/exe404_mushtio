using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IoTAgriculture.Data;
using IoTAgriculture.Models;
using IoTAgriculture.Services.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IoTAgriculture.Services
{
    public class FirebasePushNotificationService : IFirebasePushNotificationService
    {
        private static readonly TimeSpan TokenRenewBuffer = TimeSpan.FromMinutes(5);
        private static readonly string Scope = "https://www.googleapis.com/auth/firebase.messaging";
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IWebHostEnvironment _environment;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<FirebasePushNotificationService> _logger;

        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private FirebaseMessagingConfig? _config;
        private string? _accessToken;
        private DateTimeOffset _accessTokenExpiresAt = DateTimeOffset.MinValue;
        private bool _disabled;

        public FirebasePushNotificationService(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            IWebHostEnvironment environment,
            IServiceScopeFactory scopeFactory,
            ILogger<FirebasePushNotificationService> logger)
        {
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
            _environment = environment;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task SendDeviceAlertAsync(
            string deviceKey,
            string deviceName,
            string alertType,
            string metric,
            string title,
            string body,
            string severity,
            double? value,
            double? threshold,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IoTDbContext>();
            var userIds = await db.UserDevices
                .Where(x => x.DeviceKey == deviceKey)
                .Select(x => x.UserId)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (userIds.Count == 0)
            {
                _logger.LogWarning(
                    "FCM alert {AlertType} for device {DeviceKey} was not sent because the device is not assigned to any user.",
                    alertType,
                    deviceKey);
                return;
            }

            var now = DateTime.UtcNow;
            foreach (var userId in userIds)
            {
                db.UserNotifications.Add(new UserNotification
                {
                    UserNotificationId = Guid.NewGuid(),
                    UserId = userId,
                    DeviceKey = deviceKey,
                    DeviceName = deviceName,
                    AlertType = alertType,
                    MetricType = metric,
                    Severity = severity,
                    Title = title,
                    Body = body,
                    Value = value,
                    Threshold = threshold,
                    IsRead = false,
                    CreatedAt = now
                });
            }

            await db.SaveChangesAsync(cancellationToken);

            var targetTokens = await db.FcmTokens
                .Where(x => userIds.Contains(x.UserId))
                .Select(x => x.Token)
                .Distinct()
                .ToListAsync(cancellationToken);

            if (targetTokens.Count == 0)
            {
                _logger.LogWarning(
                    "FCM alert {AlertType} for device {DeviceKey} was saved in-app but not pushed because assigned users have no registered FCM token.",
                    alertType,
                    deviceKey);
                return;
            }

            if (!await EnsureEnabledAsync(cancellationToken))
            {
                _logger.LogWarning(
                    "FCM alert {AlertType} for device {DeviceKey} was not pushed because Firebase Messaging configuration is disabled or invalid.",
                    alertType,
                    deviceKey);
                return;
            }

            var config = _config!;
            var token = await GetAccessTokenAsync(config, cancellationToken);
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning(
                    "FCM alert {AlertType} for device {DeviceKey} was not pushed because a Firebase access token could not be acquired.",
                    alertType,
                    deviceKey);
                return;
            }

            foreach (var targetToken in targetTokens)
            {
                var payload = new
                {
                    message = new
                    {
                        token = targetToken,
                        notification = new
                        {
                            title,
                            body
                        },
                        data = new Dictionary<string, string>
                        {
                            ["alertType"] = alertType,
                            ["metric"] = metric,
                            ["metricType"] = metric,
                            ["deviceId"] = deviceKey,
                            ["deviceKey"] = deviceKey,
                            ["deviceName"] = deviceName,
                            ["severity"] = severity,
                            ["title"] = title,
                            ["body"] = body,
                            ["value"] = value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
                            ["threshold"] = threshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty
                        },
                        android = new
                        {
                            priority = "HIGH",
                            notification = new
                            {
                                channel_id = "sensor_alerts",
                                sound = "default"
                            }
                        },
                        apns = new
                        {
                            payload = new
                            {
                                aps = new
                                {
                                    sound = "default"
                                }
                            }
                        }
                    }
                };

                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"https://fcm.googleapis.com/v1/projects/{config.ProjectId}/messages:send");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                try
                {
                    var client = _httpClientFactory.CreateClient();
                    using var response = await client.SendAsync(request, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        var successResponseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                        _logger.LogInformation(
                            "FCM push sent successfully to token ending {TokenSuffix}: {Response}",
                            targetToken.Length <= 8 ? targetToken : targetToken[^8..],
                            successResponseBody);
                        continue;
                    }

                    var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Failed to send FCM alert to token ending {TokenSuffix}: {StatusCode} {Body}",
                        targetToken.Length <= 8 ? targetToken : targetToken[^8..],
                        response.StatusCode,
                        responseBody);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "FCM send failed for token ending {TokenSuffix}; alert processing continues.",
                        targetToken.Length <= 8 ? targetToken : targetToken[^8..]);
                }
            }
        }

        private async Task<bool> EnsureEnabledAsync(CancellationToken cancellationToken)
        {
            if (_disabled)
            {
                return false;
            }

            if (_config != null)
            {
                return true;
            }

            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                if (_config != null)
                {
                    return true;
                }

                var serviceAccountBase64 = _configuration["Firebase:Messaging:ServiceAccountBase64"]
                    ?? _configuration["Firebase:ServiceAccountBase64"]
                    ?? Environment.GetEnvironmentVariable("FIREBASE_SERVICE_ACCOUNT_BASE64")
                    ?? string.Empty;
                var serviceAccountPath = _configuration["Firebase:Messaging:ServiceAccountPath"]
                    ?? _configuration["Firebase:ServiceAccountPath"]
                    ?? string.Empty;

                JsonDocument document;
                if (!string.IsNullOrWhiteSpace(serviceAccountBase64))
                {
                    try
                    {
                        var json = Encoding.UTF8.GetString(Convert.FromBase64String(serviceAccountBase64));
                        document = JsonDocument.Parse(json);
                    }
                    catch (Exception ex) when (ex is FormatException or JsonException)
                    {
                        _logger.LogWarning(
                            ex,
                            "Firebase push notifications are disabled because service account base64 is invalid.");
                        _disabled = true;
                        return false;
                    }
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(serviceAccountPath))
                    {
                        _logger.LogWarning(
                            "Firebase service account chưa cấu hình, bỏ qua gửi push notification.");
                        _disabled = true;
                        return false;
                    }

                    var resolvedPath = Path.IsPathRooted(serviceAccountPath)
                        ? serviceAccountPath
                        : Path.Combine(_environment.ContentRootPath, serviceAccountPath);

                    if (!File.Exists(resolvedPath))
                    {
                        _logger.LogWarning(
                            "Firebase push notifications are disabled because service account file was not found at {Path}.",
                            resolvedPath);
                        _disabled = true;
                        return false;
                    }

                    await using var stream = File.OpenRead(resolvedPath);
                    document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }

                using (document)
                {
                var root = document.RootElement;

                var projectId = _configuration["Firebase:Messaging:ProjectId"];
                if (string.IsNullOrWhiteSpace(projectId) &&
                    root.TryGetProperty("project_id", out var projectIdElement))
                {
                    projectId = projectIdElement.GetString();
                }
                var clientEmail = root.TryGetProperty("client_email", out var clientEmailElement)
                    ? clientEmailElement.GetString()
                    : null;
                var privateKey = root.TryGetProperty("private_key", out var privateKeyElement)
                    ? privateKeyElement.GetString()
                    : null;
                var tokenUri = root.TryGetProperty("token_uri", out var tokenUriElement)
                    ? tokenUriElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(projectId) ||
                    string.IsNullOrWhiteSpace(clientEmail) ||
                    string.IsNullOrWhiteSpace(privateKey) ||
                    string.IsNullOrWhiteSpace(tokenUri))
                {
                    _logger.LogWarning(
                        "Firebase push notifications are disabled because the service account file is missing required fields.");
                    _disabled = true;
                    return false;
                }

                _config = new FirebaseMessagingConfig(projectId, clientEmail, privateKey, tokenUri);
                return true;
                }
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private async Task<string?> GetAccessTokenAsync(
            FirebaseMessagingConfig config,
            CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                _accessTokenExpiresAt > DateTimeOffset.UtcNow.Add(TokenRenewBuffer))
            {
                return _accessToken;
            }

            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                if (!string.IsNullOrWhiteSpace(_accessToken) &&
                    _accessTokenExpiresAt > DateTimeOffset.UtcNow.Add(TokenRenewBuffer))
                {
                    return _accessToken;
                }

                var now = DateTimeOffset.UtcNow;
                var jwt = CreateJwtAssertion(
                    config.ClientEmail,
                    config.TokenUri,
                    now,
                    now.AddMinutes(60),
                    config.PrivateKey);

                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(HttpMethod.Post, config.TokenUri)
                {
                    Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                        ["assertion"] = jwt
                    })
                };

                var response = await client.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogWarning(
                        "Failed to mint Firebase access token: {StatusCode} {Body}",
                        response.StatusCode,
                        body);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("access_token", out var accessTokenElement))
                {
                    return null;
                }

                _accessToken = accessTokenElement.GetString();
                var expiresIn = document.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                    && expiresInElement.TryGetInt32(out var seconds)
                    ? seconds
                    : 3600;
                _accessTokenExpiresAt = now.AddSeconds(expiresIn);
                _logger.LogInformation(
                    "Firebase access token acquired successfully for project {ProjectId}.",
                    config.ProjectId);
                return _accessToken;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        private static string CreateJwtAssertion(
            string clientEmail,
            string audience,
            DateTimeOffset issuedAt,
            DateTimeOffset expiresAt,
            string privateKeyPem)
        {
            var header = Base64UrlEncode(JsonSerializer.Serialize(new
            {
                alg = "RS256",
                typ = "JWT"
            }));

            var payload = Base64UrlEncode(JsonSerializer.Serialize(new
            {
                iss = clientEmail,
                scope = Scope,
                aud = audience,
                iat = issuedAt.ToUnixTimeSeconds(),
                exp = expiresAt.ToUnixTimeSeconds()
            }));

            var unsignedToken = $"{header}.{payload}";
            using var rsa = RSA.Create();
            rsa.ImportFromPem(privateKeyPem);
            var signature = rsa.SignData(
                Encoding.UTF8.GetBytes(unsignedToken),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);

            return $"{unsignedToken}.{Base64UrlEncode(signature)}";
        }

        private static string Base64UrlEncode(string value)
        {
            return Base64UrlEncode(Encoding.UTF8.GetBytes(value));
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private sealed record FirebaseMessagingConfig(
            string ProjectId,
            string ClientEmail,
            string PrivateKey,
            string TokenUri);
    }
}
