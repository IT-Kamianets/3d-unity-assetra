using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace WebArt.Assetra
{
    /// <summary>
    /// Thin client of <c>/api/assetra</c> (asset routes) and <c>/api/cloudflare</c> (file upload).
    /// Every call needs the Google ID token of the Assetra sign-in.
    /// </summary>
    public class AssetraClient
    {
        public const string DefaultApiUrl = "https://it.webart.work";
        public const string UploadContainer = "assetka";

        readonly string _api;
        readonly string _idToken;

        public AssetraClient(string idToken, string apiUrl = DefaultApiUrl)
        {
            _idToken = idToken;
            _api = apiUrl.TrimEnd('/');
        }

        public async Task<List<AssetraProduct>> ListAsync() =>
            JsonConvert.DeserializeObject<List<AssetraProduct>>(await SendAsync("GET", "/api/assetra/asset", null));

        public async Task<AssetraProduct> GetAsync(string slug) =>
            JsonConvert.DeserializeObject<AssetraProduct>(await SendAsync("GET", $"/api/assetra/asset/{Uri.EscapeDataString(slug)}", null));

        /// <summary>Creates the asset or replaces the one with the same slug.</summary>
        public async Task<AssetraProduct> PutAsync(AssetraProduct product) =>
            JsonConvert.DeserializeObject<AssetraProduct>(
                await SendAsync("PUT", $"/api/assetra/asset/{Uri.EscapeDataString(product.slug)}", JsonConvert.SerializeObject(product)));

        public Task DeleteAsync(string slug) =>
            SendAsync("DELETE", $"/api/assetra/asset/{Uri.EscapeDataString(slug)}", null);

        /// <summary>Uploads a file to Cloudflare and returns its public URL. Files above ~7 MB are rejected by the API.</summary>
        public async Task<string> UploadAsync(byte[] data, string name, string mime)
        {
            var body = JsonConvert.SerializeObject(new
            {
                container = UploadContainer,
                name,
                mime,
                base64 = Convert.ToBase64String(data),
            });
            var response = JsonConvert.DeserializeAnonymousType(
                await SendAsync("POST", "/api/cloudflare/upload-base64", body), new { publicUrl = "" });

            return response.publicUrl;
        }

        async Task<string> SendAsync(string method, string path, string json)
        {
            using var request = new UnityWebRequest(_api + path, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
            };

            request.SetRequestHeader("Authorization", "Bearer " + _idToken);

            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }

            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.SetResult(true);
            await done.Task;

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"Assetra {method} {path}: HTTP {request.responseCode} {request.downloadHandler?.text ?? request.error}");
            }

            return request.downloadHandler.text;
        }
    }
}
