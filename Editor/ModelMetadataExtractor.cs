using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace WebArt.Assetra.Editor
{
    /// <summary>
    /// Fills as many Assetra fields as Unity can know about a model: geometry, rig, animation,
    /// materials, textures, size, format and the project it comes from.
    /// What it cannot know (title, category, price, description, tags) is left for the author.
    /// </summary>
    public static class ModelMetadataExtractor
    {
        static readonly (string property, string map)[] TextureMaps =
        {
            ("_BaseMap", "albedo"), ("_MainTex", "albedo"), ("_BumpMap", "normal"),
            ("_MetallicGlossMap", "metallic"), ("_SpecGlossMap", "specular"), ("_OcclusionMap", "ao"),
            ("_EmissionMap", "emission"), ("_ParallaxMap", "height"),
        };

        /// <param name="root">The model or prefab (an asset, or an instance in a scene).</param>
        public static AssetraProduct Extract(GameObject root)
        {
            var path = AssetDatabase.GetAssetPath(root);
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            var meshes = CollectMeshes(root);
            var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
            var textures = CollectTextures(materials, out var maps);
            var bones = CollectBones(root);
            var clips = CollectClips(root, path);
            var lods = root.GetComponentsInChildren<LODGroup>(true).Select(l => l.lodCount).DefaultIfEmpty(0).Max();

            return new AssetraProduct
            {
                slug = Slugify(root.name),
                title = root.name,
                polygons = meshes.Sum(Triangles),
                rigged = bones > 0,
                animated = clips > 0,
                textures = TextureSummary(materials, textures),
                software = new List<string> { "Unity" },
                renderers = new List<string> { PipelineName() },
                formats = new List<string> { FormatOf(path) },
                archiveSize = string.IsNullOrEmpty(path) ? "" : SizeLabel(path),
                tags = new List<string> { "game:" + PlayerSettings.productName },
                details = new AssetraDetails
                {
                    vertices = meshes.Sum(m => m.vertexCount),
                    meshes = meshes.Count,
                    lods = lods > 0 ? lods : null,
                    dimensions = DimensionsOf(renderers),
                    textureResolution = ResolutionLabel(textures.Select(t => Math.Max(t.width, t.height)).DefaultIfEmpty(0).Max()),
                    pbrWorkflow = PbrWorkflow(materials),
                    textureMaps = maps.Count > 0 ? maps.ToList() : null,
                    materials = materials.Count,
                    textureCount = textures.Count,
                    bones = bones > 0 ? bones : null,
                    animations = clips > 0 ? clips : null,
                    blendShapes = meshes.Any(m => m.blendShapeCount > 0) ? true : null,
                    gameReady = true,
                    softwareVersion = "Unity " + Application.unityVersion,
                    published = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                },
            };
        }

        static List<Mesh> CollectMeshes(GameObject root)
        {
            var meshes = new List<Mesh>();

            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (filter.sharedMesh) meshes.Add(filter.sharedMesh);

            foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (skinned.sharedMesh) meshes.Add(skinned.sharedMesh);

            return meshes;
        }

        // Counts triangles (a quad counts as two); GetIndexCount does not need a readable mesh.
        static int Triangles(Mesh mesh)
        {
            var total = 0;

            for (var i = 0; i < mesh.subMeshCount; i++)
            {
                var indices = (int)mesh.GetIndexCount(i);

                total += mesh.GetTopology(i) switch
                {
                    MeshTopology.Triangles => indices / 3,
                    MeshTopology.Quads => indices / 2,
                    _ => 0,
                };
            }

            return total;
        }

        static List<Texture> CollectTextures(List<Material> materials, out HashSet<string> maps)
        {
            var textures = new List<Texture>();
            maps = new HashSet<string>();

            foreach (var material in materials)
            {
                foreach (var (property, map) in TextureMaps)
                {
                    if (material.HasProperty(property) && material.GetTexture(property) is { } texture)
                    {
                        maps.Add(map);
                        textures.Add(texture);
                    }
                }
            }

            return textures.Distinct().ToList();
        }

        static int CollectBones(GameObject root) =>
            root.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones).Where(b => b).Distinct().Count();

        // Clips of the model file itself plus those of an Animator controller on the object.
        static int CollectClips(GameObject root, string path)
        {
            var clips = new HashSet<AnimationClip>();

            if (!string.IsNullOrEmpty(path))
                foreach (var clip in AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>())
                    if (!clip.name.StartsWith("__preview__")) clips.Add(clip);

            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                if (animator.runtimeAnimatorController)
                    foreach (var clip in animator.runtimeAnimatorController.animationClips) clips.Add(clip);

            return clips.Count;
        }

        static AssetraDimensions DimensionsOf(Renderer[] renderers)
        {
            if (renderers.Length == 0) return null;

            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);

            return new AssetraDimensions
            {
                width = Math.Round(bounds.size.x, 3),
                height = Math.Round(bounds.size.y, 3),
                depth = Math.Round(bounds.size.z, 3),
                unit = "m",
            };
        }

        static string PbrWorkflow(List<Material> materials)
        {
            if (materials.Any(m => m.HasProperty("_SpecGlossMap") || m.HasProperty("_SpecColor"))) return "spec-gloss";
            if (materials.Any(m => m.HasProperty("_Metallic") || m.HasProperty("_MetallicGlossMap"))) return "metal-rough";

            return null;
        }

        static string TextureSummary(List<Material> materials, List<Texture> textures)
        {
            if (textures.Count == 0) return "";

            var workflow = PbrWorkflow(materials) != null ? "PBR" : "Basic";
            var resolution = ResolutionLabel(textures.Max(t => Math.Max(t.width, t.height)));

            return resolution == null ? workflow : $"{workflow}, {resolution}";
        }

        static string ResolutionLabel(int size) => size switch
        {
            <= 0 => null,
            <= 512 => "512",
            <= 1024 => "1K",
            <= 2048 => "2K",
            <= 4096 => "4K",
            _ => "8K",
        };

        static string PipelineName()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;

            if (pipeline == null) return "Unity Built-in";

            var name = pipeline.GetType().Name;

            return name.Contains("Universal") ? "Unity URP" : name.Contains("HD") ? "Unity HDRP" : "Unity " + name;
        }

        static string FormatOf(string path)
        {
            var extension = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

            return extension.Length > 0 ? extension : "PREFAB";
        }

        static string SizeLabel(string path)
        {
            var bytes = new FileInfo(path).Length;

            return bytes >= 1024 * 1024 ? $"{bytes / 1024 / 1024} MB" : $"{Math.Max(bytes / 1024, 1)} KB";
        }

        public static string Slugify(string name)
        {
            var slug = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) && c < 128 ? c : '-').ToArray());

            while (slug.Contains("--")) slug = slug.Replace("--", "-");

            return slug.Trim('-');
        }
    }
}
