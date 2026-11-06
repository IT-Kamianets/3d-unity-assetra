using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace WebArt.Assetra
{
    /// <summary>
    /// Thin client of <c>/api/assetra</c> (assets, projects with placeholders, models, collections) and
    /// <c>/api/cloudflare</c> (file upload). Every call needs the Firebase ID token of the Assetra sign-in
    /// (project vroom-8ef42): the Google sign-in of the Assetra site, or <c>AssetraAuth</c> in the Editor.
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

        // ---- Assets: one model an author publishes --------------------------------------------------------

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

        // ---- Projects: a game and the models it needs (its assetra.json) ----------------------------------

        /// <summary>The projects of the signed-in account (an admin sees every account's).</summary>
        public async Task<List<AssetraProject>> ListProjectsAsync() =>
            JsonConvert.DeserializeObject<List<AssetraProject>>(await SendAsync("GET", "/api/assetra/project", null));

        /// <summary>One project with its placeholders and the models that fill them.</summary>
        public async Task<AssetraProject> GetProjectAsync(string slug) =>
            JsonConvert.DeserializeObject<AssetraProject>(await SendAsync("GET", $"/api/assetra/project/{Uri.EscapeDataString(slug)}", null));

        /// <summary>Creates the project or updates its title, description and engine. Placeholders are left as they are.</summary>
        public async Task<AssetraProject> PutProjectAsync(AssetraProject project) =>
            JsonConvert.DeserializeObject<AssetraProject>(
                await SendAsync("PUT", $"/api/assetra/project/{Uri.EscapeDataString(project.slug)}", JsonConvert.SerializeObject(project)));

        /// <summary>Deletes the project and its placeholders. The models that filled them stay in Assetra.</summary>
        public Task DeleteProjectAsync(string slug) =>
            SendAsync("DELETE", $"/api/assetra/project/{Uri.EscapeDataString(slug)}", null);

        /// <summary>
        /// Sends the game's model list: <paramref name="bodyJson"/> is <c>{ title, description?, engine?, assets, prune? }</c>.
        /// The project and every placeholder are created or updated; a filled placeholder keeps its model, and
        /// placeholders missing from the list stay unless <c>prune</c> is true.
        /// </summary>
        public async Task<AssetraSyncResult> SyncProjectAsync(string slug, string bodyJson) =>
            JsonConvert.DeserializeObject<AssetraSyncResult>(
                await SendAsync("POST", $"/api/assetra/project/{Uri.EscapeDataString(slug)}/sync", bodyJson));

        public async Task<List<AssetraPlaceholder>> ListPlaceholdersAsync(string slug) =>
            JsonConvert.DeserializeObject<List<AssetraPlaceholder>>(
                await SendAsync("GET", $"/api/assetra/project/{Uri.EscapeDataString(slug)}/placeholder", null));

        /// <param name="assetId">The asset id as the game writes it, for example props/bench.</param>
        public async Task<AssetraPlaceholder> GetPlaceholderAsync(string slug, string assetId) =>
            JsonConvert.DeserializeObject<AssetraPlaceholder>(await SendAsync("GET", PlaceholderPath(slug, assetId), null));

        /// <summary>Creates or updates one placeholder (name, bounds, shape, source, metadata). The model that fills it is kept.</summary>
        public async Task<AssetraPlaceholder> PutPlaceholderAsync(string slug, AssetraPlaceholder placeholder) =>
            JsonConvert.DeserializeObject<AssetraPlaceholder>(
                await SendAsync("PUT", PlaceholderPath(slug, placeholder.id), JsonConvert.SerializeObject(placeholder)));

        /// <summary>Fills a placeholder with a model of the library; an empty <paramref name="modelId"/> empties it again.</summary>
        public async Task<AssetraPlaceholder> FillPlaceholderAsync(string slug, string assetId, string modelId) =>
            JsonConvert.DeserializeObject<AssetraPlaceholder>(
                await SendAsync("PATCH", PlaceholderPath(slug, assetId), JsonConvert.SerializeObject(new { modelId = modelId ?? "" })));

        public Task DeletePlaceholderAsync(string slug, string assetId) =>
            SendAsync("DELETE", PlaceholderPath(slug, assetId), null);

        /// <summary>The API writes an asset id into a URL with "/" as "__": props/bench becomes props__bench.</summary>
        public static string PlaceholderUrlId(string assetId) => assetId.Replace("/", "__");

        static string PlaceholderPath(string slug, string assetId) =>
            $"/api/assetra/project/{Uri.EscapeDataString(slug)}/placeholder/{Uri.EscapeDataString(PlaceholderUrlId(assetId))}";

        // ---- The shared library: models and their collections ---------------------------------------------

        public async Task<List<AssetraModel>> ListModelsAsync() =>
            JsonConvert.DeserializeObject<List<AssetraModel>>(await SendAsync("GET", "/api/assetra/model", null));

        public async Task<AssetraModel> GetModelAsync(string id) =>
            JsonConvert.DeserializeObject<AssetraModel>(await SendAsync("GET", $"/api/assetra/model/{Uri.EscapeDataString(id)}", null));

        public async Task<List<AssetraCollection>> ListCollectionsAsync() =>
            JsonConvert.DeserializeObject<List<AssetraCollection>>(await SendAsync("GET", "/api/assetra/collection", null));

        public async Task<AssetraCollection> GetCollectionAsync(string id) =>
            JsonConvert.DeserializeObject<AssetraCollection>(await SendAsync("GET", $"/api/assetra/collection/{Uri.EscapeDataString(id)}", null));

        /// <summary>Creates a collection (admin only).</summary>
        public async Task<AssetraCollection> CreateCollectionAsync(AssetraCollection collection) =>
            JsonConvert.DeserializeObject<AssetraCollection>(
                await SendAsync("POST", "/api/assetra/collection", JsonConvert.SerializeObject(collection)));

        /// <summary>Downloads a model file by the URL an Assetra model carries.</summary>
        public Task<byte[]> DownloadAsync(string url) => AssetraHttp.GetBytesAsync(url);

        Task<string> SendAsync(string method, string path, string json) =>
            AssetraHttp.SendAsync(method, _api + path, json, "application/json", _idToken);
    }
}
