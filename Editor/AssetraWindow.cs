using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace WebArt.Assetra.Editor
{
    /// <summary>
    /// Window → Assetra. Lists the assets of the signed-in account and publishes the selected model
    /// with every field Unity can extract. Until the sign-in flow exists, paste the ID token below.
    /// </summary>
    public class AssetraWindow : EditorWindow
    {
        const string TokenKey = "WebArt.Assetra.IdToken";
        const long MaxUploadBytes = 7 * 1024 * 1024;

        string _token;
        string _apiUrl = AssetraClient.DefaultApiUrl;
        GameObject _model;
        AssetraProduct _draft;
        List<AssetraProduct> _assets = new();
        string _status = "";
        Vector2 _scroll;

        [MenuItem("Window/Assetra")]
        static void Open() => GetWindow<AssetraWindow>("Assetra");

        void OnEnable() => _token = SessionState.GetString(TokenKey, "");

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            _apiUrl = EditorGUILayout.TextField("API", _apiUrl);
            EditorGUI.BeginChangeCheck();
            _token = EditorGUILayout.PasswordField("Google ID token", _token);
            if (EditorGUI.EndChangeCheck()) SessionState.SetString(TokenKey, _token);

            if (GUILayout.Button("Load my assets")) Run(LoadAssets);

            foreach (var asset in _assets)
                EditorGUILayout.LabelField($"{asset.title}  ({asset.slug})", $"{asset.polygons} tris, {asset.archiveSize}");

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Publish a model", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            _model = (GameObject)EditorGUILayout.ObjectField("Model / prefab", _model, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && _model) _draft = ModelMetadataExtractor.Extract(_model);

            if (_draft != null) DrawDraft();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(string.IsNullOrEmpty(_status) ? "Ready" : _status, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        void DrawDraft()
        {
            _draft.title = EditorGUILayout.TextField("Title", _draft.title);
            _draft.slug = EditorGUILayout.TextField("Slug", _draft.slug);
            _draft.category = EditorGUILayout.TextField("Category", _draft.category);
            _draft.price = EditorGUILayout.IntField("Price", _draft.price);
            _draft.@short = EditorGUILayout.TextField("Short", _draft.@short);
            _draft.description = EditorGUILayout.TextField("Description", _draft.description);
            EditorGUILayout.LabelField("Tags", string.Join(", ", _draft.tags));

            EditorGUILayout.LabelField("Extracted", JsonConvert.SerializeObject(_draft.details, Formatting.Indented), EditorStyles.wordWrappedLabel);

            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(_token)))
                if (GUILayout.Button("Upload and publish")) Run(Publish);
        }

        async void Run(Func<AssetraClient, Task> action)
        {
            try
            {
                _status = "Working...";
                await action(new AssetraClient(_token, _apiUrl));
            }
            catch (Exception error)
            {
                _status = error.Message;
            }

            Repaint();
        }

        async Task LoadAssets(AssetraClient client)
        {
            _assets = await client.ListAsync();
            _status = $"{_assets.Count} assets";
        }

        // Uploads the model file as it is (limited to ~7 MB by the API), then writes the asset.
        async Task Publish(AssetraClient client)
        {
            var path = AssetDatabase.GetAssetPath(_model);

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new Exception("Select a model asset from the Project window, not a scene object.");

            var data = File.ReadAllBytes(path);

            if (data.LongLength > MaxUploadBytes)
                throw new Exception("File is larger than 7 MB; large-file upload is not implemented yet.");

            _draft.model = await client.UploadAsync(data, Path.GetFileName(path), "application/octet-stream");

            var saved = await client.PutAsync(_draft);

            _status = $"Published {saved.slug}";
        }
    }
}
