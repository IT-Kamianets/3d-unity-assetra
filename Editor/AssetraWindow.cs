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
    /// Window → Assetra. Signs in with the Assetra Google account, syncs the game's assetra.json with its Assetra
    /// project (and pulls the models filled there), lists the library's collections, and publishes the selected model.
    /// </summary>
    public class AssetraWindow : EditorWindow
    {
        const long MaxUploadBytes = 7 * 1024 * 1024;
        static readonly string[] Tabs = { "Project sync", "Collections", "Publish a model" };

        string _apiUrl = AssetraClient.DefaultApiUrl;
        int _tab;
        string _status = "";
        bool _busy;
        Vector2 _scroll;

        // Sign-in
        bool _showSignIn;
        string _manualToken = "";

        // Project sync
        bool _prune;
        bool _download = true;
        AssetraSyncReport _report;
        string _pushCollection = "";

        // Collections
        List<AssetraCollection> _collections = new();
        string _newCollection = "";

        // Publish
        GameObject _model;
        AssetraProduct _draft;
        List<AssetraProduct> _assets = new();

        [MenuItem("Window/Assetra")]
        static void Open() => GetWindow<AssetraWindow>("Assetra");

        void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSignIn();
            EditorGUILayout.Space();
            _tab = GUILayout.Toolbar(_tab, Tabs);
            EditorGUILayout.Space();

            using (new EditorGUI.DisabledScope(_busy))
            {
                if (_tab == 0) DrawProjectSync();
                else if (_tab == 1) DrawCollections();
                else DrawPublish();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(_busy ? "Working..." : string.IsNullOrEmpty(_status) ? "Ready" : _status, MessageType.None);
            EditorGUILayout.EndScrollView();
        }

        // ---- Sign-in ---------------------------------------------------------------------------------------

        void DrawSignIn()
        {
            EditorGUILayout.LabelField("Assetra account", EditorStyles.boldLabel);

            if (AssetraAuth.SignedIn)
            {
                EditorGUILayout.LabelField("Signed in", string.IsNullOrEmpty(AssetraAuth.Email) ? "(Google account)" : AssetraAuth.Email);
                if (GUILayout.Button("Sign out")) AssetraAuth.SignOut();
            }
            else
            {
                using (new EditorGUI.DisabledScope(_busy || !AssetraAuth.Configured))
                    if (GUILayout.Button("Sign in with Google")) RunSignIn();

                if (!AssetraAuth.Configured)
                    EditorGUILayout.HelpBox("Set the Google OAuth client below first (Desktop client of the Assetra Google Cloud project).", MessageType.Info);
            }

            _showSignIn = EditorGUILayout.Foldout(_showSignIn, "Sign-in settings", true);
            if (!_showSignIn) return;

            _apiUrl = EditorGUILayout.TextField("API", _apiUrl);
            AssetraAuth.ClientId = EditorGUILayout.TextField("Google client id", AssetraAuth.ClientId);
            AssetraAuth.ClientSecret = EditorGUILayout.PasswordField("Google client secret", AssetraAuth.ClientSecret);

            if (GUILayout.Button("Load client JSON file..."))
            {
                var file = EditorUtility.OpenFilePanel("Google OAuth client JSON", "", "json");

                if (!string.IsNullOrEmpty(file))
                {
                    if (AssetraAuth.TryReadOauthFile(File.ReadAllText(file), out var id, out var secret))
                    {
                        AssetraAuth.ClientId = id;
                        AssetraAuth.ClientSecret = secret;
                        _status = "Google client loaded.";
                    }
                    else _status = "That file has no client_id.";
                }
            }

            EditorGUILayout.HelpBox("These stay in this machine's Editor preferences, never in the project.", MessageType.None);
            _manualToken = EditorGUILayout.PasswordField("Firebase ID token", _manualToken);

            if (GUILayout.Button("Use this token for about an hour"))
            {
                AssetraAuth.UseToken(_manualToken);
                _manualToken = "";
                _status = "Token in use.";
            }
        }

        async void RunSignIn()
        {
            _busy = true;
            Repaint();

            try
            {
                await AssetraAuth.SignInAsync();
                _status = "Signed in as " + AssetraAuth.Email;
            }
            catch (Exception error)
            {
                _status = error.Message;
            }

            _busy = false;
            Repaint();
        }

        // ---- Project sync ----------------------------------------------------------------------------------

        void DrawProjectSync()
        {
            var path = AssetraProjectFile.DefaultPath;

            if (!File.Exists(path))
            {
                EditorGUILayout.HelpBox("No assetra.json in the repository root. It lists the models the game needs.", MessageType.Info);
                return;
            }

            AssetraProjectInfo info;

            try
            {
                info = AssetraProjectFile.Info(AssetraProjectFile.Parse(File.ReadAllText(path)));
            }
            catch (Exception error)
            {
                EditorGUILayout.HelpBox("assetra.json is not valid JSON: " + error.Message, MessageType.Error);
                return;
            }

            EditorGUILayout.LabelField("Project", $"{info.Name}  ({info.Slug})");
            EditorGUILayout.LabelField("Models folder", string.IsNullOrEmpty(info.ModelsDir) ? "(none)" : info.ModelsDir);
            _download = EditorGUILayout.ToggleLeft("Download models filled in Assetra (files that exist are never overwritten)", _download);
            _prune = EditorGUILayout.ToggleLeft("Remove placeholders in Assetra that are not in assetra.json", _prune);

            if (_prune)
                EditorGUILayout.HelpBox("Prune deletes placeholders from the Assetra project (never the models). Leave it off unless the list is complete.", MessageType.Warning);

            if (GUILayout.Button("Sync project")) Run(client => Sync(client, path));

            EditorGUILayout.Space();
            _pushCollection = EditorGUILayout.TextField("Collection id (optional)", _pushCollection);
            EditorGUILayout.HelpBox("Uploads the GLB of every placeholder that is still waiting, adds it to the library and fills the placeholder. Needs an admin account; files above 7 MB are skipped.", MessageType.None);

            if (GUILayout.Button("Push local models to waiting placeholders")) Run(async client =>
            {
                _report = await AssetraPush.RunAsync(client, path, _pushCollection.Trim());
                _status = _report.Lines[0];
            });

            if (_report == null) return;

            EditorGUILayout.Space();

            foreach (var line in _report.Lines) EditorGUILayout.LabelField(line, EditorStyles.wordWrappedMiniLabel);

            if (_report.Result?.placeholders == null) return;

            EditorGUILayout.Space();

            foreach (var placeholder in _report.Result.placeholders)
                EditorGUILayout.LabelField(placeholder.id, placeholder.IsFilled ? "filled: " + placeholder.model.title : "waiting for a model");
        }

        async Task Sync(AssetraClient client, string path)
        {
            _report = await AssetraSync.RunAsync(client, path, _prune, _download);
            _status = _report.Lines.Count > 0 ? _report.Lines[0] : "Synced";
        }

        // ---- Collections -----------------------------------------------------------------------------------

        void DrawCollections()
        {
            if (GUILayout.Button("Load collections")) Run(async client =>
            {
                _collections = await client.ListCollectionsAsync();
                _status = $"{_collections.Count} collections";
            });

            foreach (var collection in _collections)
                EditorGUILayout.LabelField(collection.title, string.IsNullOrEmpty(collection.description) ? collection.id : collection.description);

            EditorGUILayout.Space();
            _newCollection = EditorGUILayout.TextField("New collection", _newCollection);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newCollection)))
                if (GUILayout.Button("Create (admin only)")) Run(async client =>
                {
                    var created = await client.CreateCollectionAsync(new AssetraCollection { title = _newCollection.Trim() });
                    _collections = await client.ListCollectionsAsync();
                    _newCollection = "";
                    _status = "Created " + created.title;
                });
        }

        // ---- Publish a model -------------------------------------------------------------------------------

        void DrawPublish()
        {
            if (GUILayout.Button("Load my assets")) Run(async client =>
            {
                _assets = await client.ListAsync();
                _status = $"{_assets.Count} assets";
            });

            foreach (var asset in _assets)
                EditorGUILayout.LabelField($"{asset.title}  ({asset.slug})", $"{asset.polygons} tris, {asset.archiveSize}");

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            _model = (GameObject)EditorGUILayout.ObjectField("Model / prefab", _model, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck() && _model) _draft = ModelMetadataExtractor.Extract(_model);

            if (_draft != null) DrawDraft();
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

            if (GUILayout.Button("Upload and publish")) Run(Publish);
        }

        // ---- Running a call --------------------------------------------------------------------------------

        // Gets a valid token (refreshing it when needed), runs the call and shows its result or its error.
        async void Run(Func<AssetraClient, Task> action)
        {
            _busy = true;
            Repaint();

            try
            {
                var token = await AssetraAuth.GetIdTokenAsync();

                if (string.IsNullOrEmpty(token)) throw new Exception("Sign in first.");

                await action(new AssetraClient(token, _apiUrl));
            }
            catch (AssetraHttpException error) when (error.Status == 401)
            {
                _status = "Assetra refused the sign-in (401). Sign in again with the Google account of the Assetra site.";
            }
            catch (AssetraHttpException error) when (error.Status == 403)
            {
                _status = "Not allowed (403): this account is not an Assetra admin, or the project belongs to someone else.";
            }
            catch (Exception error)
            {
                _status = error.Message;
            }

            _busy = false;
            Repaint();
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
