using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace WebArt.Assetra.Editor
{
    /// <summary>What a sync did, in words the window can show.</summary>
    public sealed class AssetraSyncReport
    {
        public AssetraSyncResult Result;
        public readonly List<string> Lines = new();
        public int Downloaded;
        public int Kept;
        public int Skipped;
    }

    /// <summary>
    /// Pushes the game's <c>assetra.json</c> to its Assetra project and pulls back the models that were filled there.
    /// It never removes an entry (unless prune is asked for), never overwrites a file that exists, and never writes
    /// outside the repository.
    /// </summary>
    public static class AssetraSync
    {
        public static async Task<AssetraSyncReport> RunAsync(AssetraClient client, string path, bool prune, bool download)
        {
            var doc = AssetraProjectFile.Parse(File.ReadAllText(path));
            var info = AssetraProjectFile.Info(doc);

            if (string.IsNullOrEmpty(info.Slug))
                throw new System.Exception("assetra.json has no project: set metadata.properties.assetra.project.");

            var report = new AssetraSyncReport();
            var body = AssetraProjectFile.BuildSyncBody(doc, prune);
            var result = await client.SyncProjectAsync(info.Slug, body.ToString(Newtonsoft.Json.Formatting.None));
            var placeholders = result.placeholders ?? new List<AssetraPlaceholder>();

            report.Result = result;
            report.Lines.Add($"Synced {info.Slug}: {result.written} written, {result.added} new, {result.deleted} removed.");

            var empty = placeholders.Count(p => !p.IsFilled);
            var filled = placeholders.Count - empty;
            report.Lines.Add($"{filled} filled, {empty} still waiting for a model.");

            if (download)
                await PullFilledAsync(client, path, doc, info, placeholders, report);

            return report;
        }

        static async Task PullFilledAsync(AssetraClient client, string path, JObject doc, AssetraProjectInfo info,
            List<AssetraPlaceholder> placeholders, AssetraSyncReport report)
        {
            var root = Path.GetDirectoryName(path);
            var changed = false;

            foreach (var placeholder in placeholders.Where(p => p.IsFilled))
            {
                var asset = AssetraProjectFile.FindAsset(doc, placeholder.id);

                if (asset == null)
                {
                    report.Skipped++;
                    report.Lines.Add($"skipped {placeholder.id}: not in assetra.json (kept in Assetra only)");
                    continue;
                }

                var relative = AssetraProjectFile.SourcePathFor(asset, info.ModelsDir);
                var full = AssetraProjectFile.InsideRoot(root, relative);

                if (full == null)
                {
                    report.Skipped++;
                    report.Lines.Add($"skipped {placeholder.id}: {relative} is outside the repository");
                    continue;
                }

                if (File.Exists(full))
                {
                    report.Kept++;
                    continue;
                }

                var data = await client.DownloadAsync(placeholder.model.modelUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllBytes(full, data);
                changed |= AssetraProjectFile.SetSourceIfMissing(asset, relative);
                report.Downloaded++;
                report.Lines.Add($"downloaded {placeholder.id} -> {relative} ({data.Length / 1024} KB)");
            }

            if (changed) File.WriteAllText(path, AssetraProjectFile.Serialize(doc));
            if (report.Downloaded > 0) AssetDatabase.Refresh();
            if (report.Kept > 0) report.Lines.Add($"{report.Kept} files already in the project were kept.");
        }
    }
}
