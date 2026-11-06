using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace WebArt.Assetra.Editor
{
    /// <summary>
    /// The other direction of a sync: for every placeholder still waiting for a model whose GLB exists in the game's
    /// repository, uploads the file, adds it to the library as a model (admin only) and fills the placeholder.
    /// Placeholders that already have a model are never touched, and nothing is deleted.
    /// </summary>
    public static class AssetraPush
    {
        public const long MaxUploadBytes = 7 * 1024 * 1024;

        public static async Task<AssetraSyncReport> RunAsync(AssetraClient client, string path, string collectionId)
        {
            var doc = AssetraProjectFile.Parse(File.ReadAllText(path));
            var info = AssetraProjectFile.Info(doc);

            if (string.IsNullOrEmpty(info.Slug))
                throw new System.Exception("assetra.json has no project: set metadata.properties.assetra.project.");

            var report = new AssetraSyncReport();
            var root = Path.GetDirectoryName(path);
            var project = await client.GetProjectAsync(info.Slug);
            var waiting = (project.placeholders ?? new List<AssetraPlaceholder>()).Where(p => !p.IsFilled).ToList();
            var pushed = 0;

            report.Lines.Add($"{waiting.Count} placeholders are waiting for a model.");

            foreach (var placeholder in waiting)
            {
                var asset = AssetraProjectFile.FindAsset(doc, placeholder.id);
                var uri = asset == null ? null : (string)asset["source"]?["uri"];
                var full = AssetraProjectFile.InsideRoot(root, uri);

                if (full == null || !File.Exists(full))
                {
                    report.Skipped++;
                    report.Lines.Add($"skipped {placeholder.id}: no model file in the repository");
                    continue;
                }

                var data = File.ReadAllBytes(full);

                if (data.Length > MaxUploadBytes)
                {
                    report.Skipped++;
                    report.Lines.Add($"skipped {placeholder.id}: {data.Length / 1024} KB is above the 7 MB upload limit");
                    continue;
                }

                var fileName = Path.GetFileName(full);
                var url = await client.UploadAsync(data, $"{info.Slug}-{fileName}", "model/gltf-binary");
                var model = await client.CreateModelAsync(new AssetraModel
                {
                    title = string.IsNullOrEmpty(placeholder.name) ? placeholder.id : placeholder.name,
                    category = Category(placeholder.id),
                    collectionId = collectionId ?? "",
                    modelUrl = url,
                    fileName = fileName,
                    size = data.Length,
                });

                await client.FillPlaceholderAsync(info.Slug, placeholder.id, model.id);
                pushed++;
                report.Lines.Add($"pushed {placeholder.id} ({data.Length / 1024} KB)");
            }

            report.Downloaded = pushed;
            report.Lines.Insert(0, $"Pushed {pushed} models to {info.Slug}.");
            return report;
        }

        // "props/bench" -> "props"
        static string Category(string id)
        {
            var slash = id.IndexOf('/');
            return slash > 0 ? id.Substring(0, slash) : "";
        }
    }
}
