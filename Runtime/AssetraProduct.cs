using System.Collections.Generic;
using Newtonsoft.Json;

namespace WebArt.Assetra
{
    /// <summary>
    /// One Assetra asset (Firestore <c>assets/{slug}</c>). Mirrors <c>Product</c> of the website;
    /// nullable fields are left out of the JSON when they are not known.
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class AssetraProduct
    {
        public string slug;
        public string title;
        public string category = "";
        public string seller = "";
        public int price;
        public string @short = "";
        public string description = "";
        public List<string> images = new();
        public string model;
        public string modelLow;
        public List<string> purpose = new() { "games" };
        public int polygons;
        public string textures = "";
        public bool rigged;
        public bool animated;
        public List<string> tags = new();
        public List<string> software = new();
        public List<string> renderers = new();
        public List<string> formats = new();
        public string archiveSize = "";
        public AssetraDetails details;

        // Set by the server; read-only for clients.
        public string uid;
        public long? updatedAt;
    }

    /// <summary>Technical details (ProductDetails of the website). Only filled fields are sent.</summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class AssetraDetails
    {
        public int? vertices;
        public string geometry;
        public int? meshes;
        public int? lods;
        public string style;
        public AssetraDimensions dimensions;
        public bool? realWorldScale;
        public bool? watertight;
        public string textureResolution;
        public string pbrWorkflow;
        public List<string> textureMaps;
        public int? materials;
        public int? textureCount;
        public string uv;
        public bool? udim;
        public int? bones;
        public int? animations;
        public bool? blendShapes;
        public bool? gameReady;
        public bool? arReady;
        public bool? vrReady;
        public bool? printable;
        public string license;
        public string softwareVersion;
        public string plugins;
        public string published;
    }

    public class AssetraDimensions
    {
        public double width;
        public double height;
        public double depth;
        public string unit = "m";
    }
}
