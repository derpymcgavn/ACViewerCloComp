using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using ACViewer.Config;

using System.Threading.Tasks;

namespace ACViewer.Services
{
    public sealed class DerpAceAdminClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
        private readonly Dictionary<uint, string> _versions = new Dictionary<uint, string>();

        public static DerpAceAdminClient Instance { get; } = new DerpAceAdminClient();
        public Uri BaseUri { get; private set; }
        public string Token { get; private set; }
        public bool AllowInsecureRemoteHttp { get; private set; }

        private DerpAceAdminClient() { }

        public DerpAceConnectionProfile LoadProfile()
        {
            try
            {
                if (File.Exists(ProfilePath))
                    return JsonSerializer.Deserialize<DerpAceConnectionProfile>(File.ReadAllText(ProfilePath), JsonOptions) ?? new DerpAceConnectionProfile();
            }
            catch { }
            return new DerpAceConnectionProfile();
        }

        public void Configure(string baseUrl, string token, bool allowInsecureRemoteHttp)
        {
            if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Enter an absolute http:// or https:// DerpACE admin URL.");

            if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback && !allowInsecureRemoteHttp)
                throw new InvalidOperationException("Remote HTTP exposes the admin token. Use HTTPS/VPN, or explicitly allow insecure LAN HTTP for this profile.");
            if (string.IsNullOrWhiteSpace(token))
                throw new InvalidOperationException("Enter the DerpACE admin-map token. It is kept only for this app session.");

            var normalized = uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri.AbsoluteUri + "/");
            BaseUri = normalized;
            Token = token;
            AllowInsecureRemoteHttp = allowInsecureRemoteHttp;
            SaveProfile(new DerpAceConnectionProfile { BaseUrl = normalized.AbsoluteUri.TrimEnd('/'), AllowInsecureRemoteHttp = allowInsecureRemoteHttp });
        }

        public async Task<IReadOnlyList<DerpAceClothingDocument>> ListAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendAsync<DerpAceClothingListResponse>(HttpMethod.Get, "api/clothing", null, cancellationToken);
            return response.Entries ?? new List<DerpAceClothingDocument>();
        }

        public async Task<DerpAceClothingDocument> GetAsync(uint id, CancellationToken cancellationToken = default)
        {
            var response = await SendAsync<DerpAceClothingDocumentResponse>(HttpMethod.Get, $"api/clothing?id=0x{id:X8}", null, cancellationToken);
            if (response.Document == null)
                throw new InvalidOperationException("DerpACE returned an empty ClothingBase document.");
            if (!string.IsNullOrWhiteSpace(response.Document.Sha256))
                _versions[id] = response.Document.Sha256;
            return response.Document;
        }

        public async Task<DerpAceClothingDocument> PublishAsync(uint id, string json, bool force, CancellationToken cancellationToken = default)
        {
            _versions.TryGetValue(id, out var expectedSha256);
            var request = new DerpAceClothingSaveRequest { Json = json, ExpectedSha256 = expectedSha256, Force = force };
            var response = await SendAsync<DerpAceClothingDocumentResponse>(HttpMethod.Post, "api/clothing", request, cancellationToken);
            if (response.Document == null)
                throw new InvalidOperationException("DerpACE saved the document but returned no version information.");
            _versions[id] = response.Document.Sha256;
            return response.Document;
        }

        public async Task<string> ReloadAsync(CancellationToken cancellationToken = default)
        {
            var response = await SendAsync<DerpAceResponse>(HttpMethod.Post, "api/clothing/reload", new { }, cancellationToken);
            return response.Message ?? "DerpACE ClothingBase content reloaded.";
        }

        private async Task<T> SendAsync<T>(HttpMethod method, string relativePath, object body, CancellationToken cancellationToken) where T : DerpAceResponse
        {
            if (BaseUri == null || string.IsNullOrWhiteSpace(Token))
                throw new InvalidOperationException("Connect to DerpACE first.");

            using var request = new HttpRequestMessage(method, new Uri(BaseUri, relativePath));
            request.Headers.TryAddWithoutValidation("X-DerpACE-Map-Token", Token);
            request.Headers.TryAddWithoutValidation("User-Agent", "DerpACE-Clothing-Studio/1.0");
            if (body != null)
                request.Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            T result = null;
            try { result = JsonSerializer.Deserialize<T>(payload, JsonOptions); } catch { }
            if (!response.IsSuccessStatusCode || result?.Ok == false)
                throw new DerpAceAdminException(result?.Error ?? $"DerpACE returned {(int)response.StatusCode} {response.ReasonPhrase}.", (int)response.StatusCode);
            return result ?? throw new InvalidOperationException("DerpACE returned an invalid response.");
        }

        private static string ProfilePath
        {
            get
            {
                var directory = ConfigManager.AppDataDirectory;
                return Path.Combine(directory, "DerpAceAdmin.json");
            }
        }

        private static void SaveProfile(DerpAceConnectionProfile profile)
        {
            // The URL and transport choice are harmless convenience settings. Admin tokens are never written to disk.
            File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profile, JsonOptions));
        }
    }

    public sealed class DerpAceConnectionProfile
    {
        public string BaseUrl { get; set; } = "http://127.0.0.1:9110";
        public bool AllowInsecureRemoteHttp { get; set; }
    }

    public class DerpAceResponse
    {
        public bool Ok { get; set; }
        public string Message { get; set; }
        public string Error { get; set; }
    }

    public sealed class DerpAceClothingListResponse : DerpAceResponse
    {
        public List<DerpAceClothingDocument> Entries { get; set; }
    }

    public sealed class DerpAceClothingDocumentResponse : DerpAceResponse
    {
        public DerpAceClothingDocument Document { get; set; }
    }

    public sealed class DerpAceClothingDocument
    {
        public uint Id { get; set; }
        public string IdHex { get; set; }
        public string FileName { get; set; }
        public string Json { get; set; }
        public string Sha256 { get; set; }
        public long Size { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public bool IsCustom { get; set; }
        public bool AllowBaseOverride { get; set; }
        public string DisplayName => $"{(string.IsNullOrWhiteSpace(IdHex) ? $"0x{Id:X8}" : IdHex)}  {FileName ?? "portal.dat"}";
    }

    internal sealed class DerpAceClothingSaveRequest
    {
        public string Json { get; set; }
        public string ExpectedSha256 { get; set; }
        public bool Force { get; set; }
    }

    public sealed class DerpAceAdminException : Exception
    {
        public int StatusCode { get; }
        public DerpAceAdminException(string message, int statusCode) : base(message) => StatusCode = statusCode;
    }
}
