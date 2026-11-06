using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using WebArt.Assetra.Editor;

namespace WebArt.Assetra.Tests
{
    public class AssetraAuthTests
    {
        [Test]
        public void The_pkce_challenge_is_the_sha256_of_the_verifier_in_base64url()
        {
            // The example from RFC 7636, appendix B.
            Assert.AreEqual("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", AssetraAuth.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        }

        [Test]
        public void A_new_verifier_is_url_safe_long_enough_and_different_each_time()
        {
            var a = AssetraAuth.NewVerifier();
            var b = AssetraAuth.NewVerifier();
            Assert.GreaterOrEqual(a.Length, 43);
            Assert.AreNotEqual(a, b);
            StringAssert.DoesNotContain("+", a);
            StringAssert.DoesNotContain("/", a);
            StringAssert.DoesNotContain("=", a);
        }

        [Test]
        public void The_google_url_asks_for_a_code_with_pkce_and_the_state()
        {
            var url = AssetraAuth.AuthUrl("client-1", "http://127.0.0.1:5000/", "challenge", "state-1");
            StringAssert.StartsWith(AssetraAuth.GoogleAuthEndpoint, url);
            StringAssert.Contains("client_id=client-1", url);
            StringAssert.Contains("response_type=code", url);
            StringAssert.Contains("code_challenge=challenge", url);
            StringAssert.Contains("code_challenge_method=S256", url);
            StringAssert.Contains("state=state-1", url);
            StringAssert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A5000%2F", url);
        }

        [Test]
        public void The_code_exchange_has_the_secret_only_when_there_is_one()
        {
            StringAssert.Contains("client_secret=s3", AssetraAuth.ExchangeBody("c", "s3", "code", "ver", "http://127.0.0.1:1/"));
            StringAssert.DoesNotContain("client_secret", AssetraAuth.ExchangeBody("c", "", "code", "ver", "http://127.0.0.1:1/"));
        }

        [Test]
        public void Firebase_is_asked_to_sign_in_with_the_google_id_token()
        {
            var body = JObject.Parse(AssetraAuth.IdpBody("g.tok"));
            StringAssert.Contains("id_token=g.tok", (string)body["postBody"]);
            StringAssert.Contains("providerId=google.com", (string)body["postBody"]);
            Assert.IsTrue((bool)body["returnSecureToken"]);
            StringAssert.Contains("signInWithIdp?key=", AssetraAuth.IdpUrl("K"));
            StringAssert.Contains("securetoken.googleapis.com", AssetraAuth.RefreshUrl("K"));
            Assert.AreEqual("grant_type=refresh_token&refresh_token=r%2F1", AssetraAuth.RefreshBody("r/1"));
        }

        [TestCase("{\"installed\":{\"client_id\":\"a\",\"client_secret\":\"b\"}}", "a", "b")]
        [TestCase("{\"web\":{\"client_id\":\"a\",\"client_secret\":\"b\"}}", "a", "b")]
        [TestCase("{\"client_id\":\"a\"}", "a", "")]
        public void The_oauth_client_file_is_read_in_its_three_shapes(string json, string id, string secret)
        {
            Assert.IsTrue(AssetraAuth.TryReadOauthFile(json, out var gotId, out var gotSecret));
            Assert.AreEqual(id, gotId);
            Assert.AreEqual(secret, gotSecret);
        }

        [TestCase("not json")]
        [TestCase("{\"installed\":{}}")]
        public void A_file_without_a_client_id_is_refused(string json)
        {
            Assert.IsFalse(AssetraAuth.TryReadOauthFile(json, out _, out _));
        }
    }

    public class AssetraProjectFileTests
    {
        const string Doc = @"{
  ""schemaVersion"": ""1.0.0"", ""libraryId"": ""math-runner"", ""units"": ""meters"",
  ""metadata"": { ""properties"": { ""assetra"": { ""format"": 1, ""project"": ""math-runner"", ""name"": ""Math Runner"", ""modelsDir"": ""Assets/MathRunner/Art/Models"" } } },
  ""assets"": [
    { ""id"": ""props/bench"", ""kind"": ""model"", ""name"": ""Bench"", ""source"": { ""uri"": ""Assets/MathRunner/Art/Models/15_bench.glb"", ""format"": ""glb"" } },
    { ""id"": ""props/lamp"", ""name"": ""Lamp"" },
    { ""id"": ""sounds/click"", ""kind"": ""sound"" }
  ]
}";

        [Test]
        public void The_project_comes_from_the_assetra_metadata()
        {
            var info = AssetraProjectFile.Info(JObject.Parse(Doc));
            Assert.AreEqual("math-runner", info.Slug);
            Assert.AreEqual("Math Runner", info.Name);
            Assert.AreEqual("Assets/MathRunner/Art/Models", info.ModelsDir);
        }

        [Test]
        public void Without_assetra_metadata_the_library_id_is_the_project()
        {
            var info = AssetraProjectFile.Info(JObject.Parse("{\"libraryId\":\"temnorid-arena\",\"assets\":[]}"));
            Assert.AreEqual("temnorid-arena", info.Slug);
            Assert.AreEqual("temnorid-arena", info.Name);
        }

        [Test]
        public void The_sync_body_has_the_models_only_and_never_prunes_unless_asked()
        {
            var body = AssetraProjectFile.BuildSyncBody(JObject.Parse(Doc), false);
            Assert.AreEqual("Math Runner", (string)body["title"]);
            Assert.AreEqual("unity", (string)body["engine"]);
            Assert.IsFalse((bool)body["prune"]);
            var ids = ((JArray)body["assets"]).Select(a => (string)a["id"]).ToArray();
            CollectionAssert.AreEqual(new[] { "props/bench", "props/lamp" }, ids);
            Assert.IsTrue((bool)AssetraProjectFile.BuildSyncBody(JObject.Parse(Doc), true)["prune"]);
        }

        [Test]
        public void A_model_goes_to_its_own_source_or_to_the_models_folder_by_its_id()
        {
            var doc = JObject.Parse(Doc);
            Assert.AreEqual("Assets/MathRunner/Art/Models/15_bench.glb", AssetraProjectFile.SourcePathFor(AssetraProjectFile.FindAsset(doc, "props/bench"), "x"));
            Assert.AreEqual("Assets/MathRunner/Art/Models/props_lamp.glb", AssetraProjectFile.SourcePathFor(AssetraProjectFile.FindAsset(doc, "props/lamp"), "Assets/MathRunner/Art/Models/"));
        }

        [Test]
        public void The_source_is_set_once_and_an_existing_one_is_never_changed()
        {
            var doc = JObject.Parse(Doc);
            var lamp = AssetraProjectFile.FindAsset(doc, "props/lamp");
            Assert.IsTrue(AssetraProjectFile.SetSourceIfMissing(lamp, "a.glb"));
            Assert.AreEqual("a.glb", (string)lamp["source"]["uri"]);
            Assert.IsFalse(AssetraProjectFile.SetSourceIfMissing(lamp, "b.glb"));
            Assert.AreEqual("a.glb", (string)lamp["source"]["uri"]);
        }

        [Test]
        public void A_model_is_never_written_outside_the_repository()
        {
            var root = Path.Combine(Path.GetTempPath(), "assetra-root");
            Assert.IsNotNull(AssetraProjectFile.InsideRoot(root, "Assets/Models/a.glb"));
            Assert.IsNull(AssetraProjectFile.InsideRoot(root, "../outside.glb"));
            Assert.IsNull(AssetraProjectFile.InsideRoot(root, "Assets/../../outside.glb"));
            Assert.IsNull(AssetraProjectFile.InsideRoot(root, Path.Combine(Path.GetTempPath(), "abs.glb")));
            Assert.IsNull(AssetraProjectFile.InsideRoot(root, ""));
        }

        [Test]
        public void A_placeholder_url_id_writes_the_slash_as_two_underscores()
        {
            Assert.AreEqual("props__bench", AssetraClient.PlaceholderUrlId("props/bench"));
        }

        [Test]
        public void A_placeholder_is_filled_only_with_a_model_url()
        {
            var json = "{\"id\":\"a\",\"status\":\"filled\",\"model\":{\"id\":\"m\",\"title\":\"T\",\"modelUrl\":\"https://cdn-it.webart.work/assetka/a.glb\"}}";
            Assert.IsTrue(JsonConvert.DeserializeObject<AssetraPlaceholder>(json).IsFilled);
            Assert.IsFalse(JsonConvert.DeserializeObject<AssetraPlaceholder>("{\"id\":\"a\",\"status\":\"placeholder\",\"model\":null}").IsFilled);
        }
    }
}
