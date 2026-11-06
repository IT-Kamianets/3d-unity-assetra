using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WebArt.Assetra.Editor
{
    /// <summary>The project part of <c>assetra.json</c>: <c>metadata.properties.assetra</c>.</summary>
    public sealed class AssetraProjectInfo
    {
        public string Slug;
        public string Name;
        public string ModelsDir;
    }

    /// <summary>
    /// Reads and writes the game's <c>assetra.json</c>: a 3d-asset-schema library in the repository root that
    /// lists every model the game needs. The game owns it; this class only reads it, builds what a sync sends,
    /// and fills in the <c>source</c> of an asset once its model has been downloaded.
    /// </summary>
    public static class AssetraProjectFile
    {
        public const string FileName = "assetra.json";

        /// <summary>The repository root: the folder above Assets.</summary>
        public static string ProjectRoot => Directory.GetParent(Application.dataPath)!.FullName;

        public static string DefaultPath => Path.Combine(ProjectRoot, FileName);

        public static JObject Parse(string json) => JObject.Parse(json);

        public static string Serialize(JObject doc) => doc.ToString(Formatting.Indented) + "\n";

        public static AssetraProjectInfo Info(JObject doc)
        {
            var node = doc["metadata"]?["properties"]?["assetra"] as JObject;
            var slug = (string)node?["project"];
            if (string.IsNullOrEmpty(slug)) slug = (string)doc["libraryId"];

            return new AssetraProjectInfo
            {
                Slug = slug,
                Name = (string)node?["name"] ?? slug,
                ModelsDir = (string)node?["modelsDir"] ?? "",
            };
        }

        /// <summary>
        /// What a sync sends: <c>{ title, engine, assets, prune }</c>. Only assets of kind "model" (or with no kind)
        /// go; the server ignores the others anyway, and so a bad entry of another kind cannot fail the sync.
        /// </summary>
        public static JObject BuildSyncBody(JObject doc, bool prune)
        {
            var info = Info(doc);
            var assets = new JArray(((doc["assets"] as JArray) ?? new JArray())
                .Where(asset => asset is JObject o && ((string)o["kind"] == null || (string)o["kind"] == "model")));

            return new JObject
            {
                ["title"] = info.Name,
                ["engine"] = "unity",
                ["assets"] = assets,
                ["prune"] = prune,
            };
        }

        /// <summary>The asset with this id in the file, or null.</summary>
        public static JObject FindAsset(JObject doc, string id) =>
            (doc["assets"] as JArray)?.OfType<JObject>().FirstOrDefault(asset => (string)asset["id"] == id);

        /// <summary>
        /// Where a model goes, relative to the file: the asset's own <c>source.uri</c>, or <c>modelsDir/&lt;id&gt;.glb</c>
        /// with "/" in the id written as "_" when the asset has none yet.
        /// </summary>
        public static string SourcePathFor(JObject asset, string modelsDir)
        {
            var uri = (string)asset["source"]?["uri"];
            if (!string.IsNullOrEmpty(uri)) return uri;

            var name = ((string)asset["id"]).Replace('/', '_') + ".glb";
            return string.IsNullOrEmpty(modelsDir) ? name : modelsDir.TrimEnd('/') + "/" + name;
        }

        /// <summary>Sets <c>source</c> on an asset that has none. Returns true when the file changed.</summary>
        public static bool SetSourceIfMissing(JObject asset, string uri)
        {
            if (!string.IsNullOrEmpty((string)asset["source"]?["uri"])) return false;
            asset["source"] = new JObject { ["uri"] = uri, ["format"] = "glb" };
            return true;
        }

        /// <summary>
        /// The full path of <paramref name="relativeUri"/> inside <paramref name="root"/>, or null when it would
        /// leave the folder: a model is never written outside the game's repository.
        /// </summary>
        public static string InsideRoot(string root, string relativeUri)
        {
            if (string.IsNullOrEmpty(relativeUri) || Path.IsPathRooted(relativeUri)) return null;

            var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, relativeUri));
            return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
    }
}
