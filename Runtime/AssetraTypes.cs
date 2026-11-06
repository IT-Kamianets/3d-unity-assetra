using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace WebArt.Assetra
{
    /// <summary>The file a placeholder is kept in inside the game (<c>source</c> of an assetra.json asset).</summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class AssetraSource
    {
        public string uri;
        public string format = "glb";
    }

    /// <summary>The model that fills a placeholder, as the API embeds it.</summary>
    public class AssetraModelRef
    {
        public string id;
        public string title;
        public string modelUrl;
        public string lowModelUrl;
        public string imageUrl;
        public string fileName;
        public long? size;
    }

    /// <summary>
    /// One model a game needs (an asset of kind "model" in its assetra.json). <see cref="status"/> is
    /// "placeholder" while no model fills it and "filled" once <see cref="model"/> is set.
    /// </summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class AssetraPlaceholder
    {
        public string id;
        public string kind = "model";
        public string name;
        public JObject bounds;
        public JObject placeholder;
        public AssetraSource source;
        public JObject metadata;

        // Set by the server.
        public string modelId;
        public AssetraModelRef model;
        public string status;
        public long? createdAt;
        public long? updatedAt;

        [JsonIgnore]
        public bool IsFilled => status == "filled" && model != null && !string.IsNullOrEmpty(model.modelUrl);
    }

    /// <summary>A game in Assetra (Firestore <c>projects/{slug}</c>).</summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class AssetraProject
    {
        public string slug;
        public string title;
        public string description = "";
        public string engine = "";
        public string uid;
        public long? createdAt;
        public long? updatedAt;
        public List<AssetraPlaceholder> placeholders;
    }

    /// <summary>The answer of a sync: the project with its placeholders, and what the sync did.</summary>
    public class AssetraSyncResult : AssetraProject
    {
        public int written;
        public int added;
        public int deleted;
    }

    /// <summary>A model in the shared Assetra library.</summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class AssetraModel
    {
        public string id;
        public string title;
        public string description;
        public string category;
        public string collectionId;
        public string modelUrl;
        public string lowModelUrl;
        public string imageUrl;
        public string fileName;
        public long? size;
        public string uid;
    }

    /// <summary>A collection of models, public to read, admin to write.</summary>
    [JsonObject(ItemNullValueHandling = NullValueHandling.Ignore)]
    public class AssetraCollection
    {
        public string id;
        public string title;
        public string description;
        public string imageUrl;
        public bool? vroom;
        public string uid;
    }
}
