using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IoTAgriculture.Services.Interfaces;

namespace IoTAgriculture.Services
{
    public class FirebaseRtdbService : IFirebaseRtdbService
    {
        private readonly HttpClient _http;
        private readonly string _baseUrl;
        private readonly string? _authToken;
        private readonly JsonSerializerOptions _jsonOptions;

        public FirebaseRtdbService(HttpClient http, IConfiguration config)
        {
            _http = http;
            _baseUrl = (config["Firebase:DatabaseUrl"] ?? string.Empty).TrimEnd('/');
            _authToken = config["Firebase:AuthToken"];

            if (string.IsNullOrWhiteSpace(_baseUrl))
            {
                throw new InvalidOperationException("Firebase:DatabaseUrl is missing in appsettings.json.");
            }

            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
        }

        public async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken = default)
        {
            var response = await _http.GetAsync(BuildUrl(path), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
        }

        public async Task SetAsync<T>(string path, T value, CancellationToken cancellationToken = default)
        {
            var response = await _http.PutAsJsonAsync(BuildUrl(path), value, _jsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task PatchAsync<T>(string path, T value, CancellationToken cancellationToken = default)
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), BuildUrl(path))
            {
                Content = JsonContent.Create(value, options: _jsonOptions)
            };

            var response = await _http.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
        }

        public async Task<string> PushAsync<T>(string path, T value, CancellationToken cancellationToken = default)
        {
            var response = await _http.PostAsJsonAsync(BuildUrl(path), value, _jsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<FirebasePushResponse>(
                _jsonOptions,
                cancellationToken);

            return result?.Name ?? string.Empty;
        }

        private string BuildUrl(string path)
        {
            var cleanPath = path.Trim('/');
            var url = string.IsNullOrEmpty(cleanPath)
                ? $"{_baseUrl}/.json"
                : $"{_baseUrl}/{cleanPath}.json";

            if (!string.IsNullOrWhiteSpace(_authToken))
            {
                url += $"?auth={_authToken}";
            }

            return url;
        }

        private sealed class FirebasePushResponse
        {
            public string? Name { get; set; }
        }
    }
}
