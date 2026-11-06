using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace WebArt.Assetra.Editor
{
    /// <summary>
    /// Signs the developer in to Assetra from the Editor, with the same Google account as the Assetra site.
    /// The browser signs in to Google, Google redirects to a one-shot listener on 127.0.0.1, the code is exchanged
    /// (PKCE) for a Google ID token, and Firebase (project vroom-8ef42) turns it into the Firebase ID token the
    /// API accepts. The API only accepts tokens of that project, so a Lords or other Firebase token is refused.
    ///
    /// Google needs an OAuth client of type Desktop in the Google Cloud project of vroom-8ef42. Its client id and
    /// secret are stored per developer in the Editor preferences (Window → Assetra → Sign-in), never in the project.
    /// </summary>
    public static class AssetraAuth
    {
        /// <summary>Public web configuration of the Assetra Firebase project, the same one the Assetra site ships.</summary>
        public const string FirebaseApiKey = "AIzaSyDDvvPlZfa7RSmpZUaqHTHqu7LK9yXgQxc";

        public const string GoogleAuthEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
        public const string GoogleTokenUrl = "https://oauth2.googleapis.com/token";

        const string ClientIdKey = "WebArt.Assetra.GoogleClientId";
        const string ClientSecretKey = "WebArt.Assetra.GoogleClientSecret";
        const string RefreshKey = "WebArt.Assetra.RefreshToken";
        const string EmailKey = "WebArt.Assetra.Email";
        const string IdTokenKey = "WebArt.Assetra.FirebaseIdToken";
        const string ExpiryKey = "WebArt.Assetra.FirebaseIdTokenExpiry";

        public static string ClientId
        {
            get => EditorPrefs.GetString(ClientIdKey, "");
            set => EditorPrefs.SetString(ClientIdKey, (value ?? "").Trim());
        }

        public static string ClientSecret
        {
            get => EditorPrefs.GetString(ClientSecretKey, "");
            set => EditorPrefs.SetString(ClientSecretKey, (value ?? "").Trim());
        }

        /// <summary>True when a Google OAuth client is set, so "Sign in with Google" can start.</summary>
        public static bool Configured => !string.IsNullOrEmpty(ClientId);

        /// <summary>True when this machine remembers a sign-in (a refresh token is saved).</summary>
        public static bool SignedIn => !string.IsNullOrEmpty(EditorPrefs.GetString(RefreshKey, ""));

        public static string Email => EditorPrefs.GetString(EmailKey, "");

        // ---- Sign in ---------------------------------------------------------------------------------------

        /// <summary>Opens the browser, waits for Google's answer and returns the Firebase ID token. Throws with a readable message.</summary>
        public static async Task<string> SignInAsync()
        {
            if (!Configured) throw new InvalidOperationException("Set the Google OAuth client first (Sign-in settings).");

            var googleToken = await GoogleIdTokenAsync();
            var answer = JObject.Parse(await AssetraHttp.SendAsync("POST", IdpUrl(FirebaseApiKey), IdpBody(googleToken)));
            var idToken = (string)answer["idToken"];
            var refresh = (string)answer["refreshToken"];

            if (string.IsNullOrEmpty(idToken) || string.IsNullOrEmpty(refresh))
                throw new InvalidOperationException("Firebase did not return a sign-in.");

            Remember(idToken, refresh, (string)answer["expiresIn"], (string)answer["email"]);
            return idToken;
        }

        /// <summary>A valid Firebase ID token: the cached one, or a fresh one from the saved refresh token. Null when not signed in.</summary>
        public static async Task<string> GetIdTokenAsync()
        {
            var cached = SessionState.GetString(IdTokenKey, "");
            if (!string.IsNullOrEmpty(cached) && ExpiryOf(SessionState.GetString(ExpiryKey, "0")) > DateTime.UtcNow.AddMinutes(2)) return cached;

            var refresh = EditorPrefs.GetString(RefreshKey, "");
            if (string.IsNullOrEmpty(refresh)) return null;

            try
            {
                var answer = JObject.Parse(await AssetraHttp.SendAsync("POST", RefreshUrl(FirebaseApiKey), RefreshBody(refresh), "application/x-www-form-urlencoded"));
                var idToken = (string)answer["id_token"];
                Remember(idToken, (string)answer["refresh_token"] ?? refresh, (string)answer["expires_in"], Email);
                return idToken;
            }
            catch (AssetraHttpException)
            {
                // The refresh token was revoked or expired: the developer has to sign in again.
                SignOut();
                return null;
            }
        }

        public static void SignOut()
        {
            EditorPrefs.DeleteKey(RefreshKey);
            EditorPrefs.DeleteKey(EmailKey);
            SessionState.EraseString(IdTokenKey);
            SessionState.EraseString(ExpiryKey);
        }

        /// <summary>For a developer who has a token from the Assetra site: use it until it expires, without Google sign-in.</summary>
        public static void UseToken(string idToken)
        {
            SessionState.SetString(IdTokenKey, idToken ?? "");
            SessionState.SetString(ExpiryKey, DateTimeOffset.UtcNow.AddMinutes(55).ToUnixTimeSeconds().ToString());
        }

        static void Remember(string idToken, string refresh, string expiresInSeconds, string email)
        {
            var seconds = int.TryParse(expiresInSeconds, out var value) ? value : 3600;
            SessionState.SetString(IdTokenKey, idToken);
            SessionState.SetString(ExpiryKey, DateTimeOffset.UtcNow.AddSeconds(seconds).ToUnixTimeSeconds().ToString());
            EditorPrefs.SetString(RefreshKey, refresh);
            if (!string.IsNullOrEmpty(email)) EditorPrefs.SetString(EmailKey, email);
        }

        static DateTime ExpiryOf(string unixSeconds) =>
            long.TryParse(unixSeconds, out var value) ? DateTimeOffset.FromUnixTimeSeconds(value).UtcDateTime : DateTime.MinValue;

        // ---- Google's installed-app flow -----------------------------------------------------------------

        static async Task<string> GoogleIdTokenAsync()
        {
            var verifier = NewVerifier();
            var state = NewVerifier();
            var port = FreePort();
            var redirect = "http://127.0.0.1:" + port + "/";

            using var listener = new HttpListener();
            listener.Prefixes.Add(redirect);

            try
            {
                listener.Start();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("Could not listen for Google's answer: " + exception.Message);
            }

            Application.OpenURL(AuthUrl(ClientId, redirect, Challenge(verifier), state));

            var waiting = listener.GetContextAsync();
            var first = await Task.WhenAny(waiting, Task.Delay(TimeSpan.FromMinutes(3)));
            if (first != waiting) throw new TimeoutException("Google sign-in timed out.");

            var context = await waiting;
            var query = context.Request.QueryString;
            var code = query["code"];
            var error = query["error"];
            var good = code != null && query["state"] == state;

            var page = Encoding.UTF8.GetBytes("<html><body style=\"font-family:sans-serif;background:#121111;color:#eee;text-align:center;padding-top:20vh\"><h2>"
                + (good ? "Signed in. You can close this window and go back to Unity." : "Sign-in did not finish. You can close this window.")
                + "</h2></body></html>");
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.OutputStream.Write(page, 0, page.Length);
            context.Response.Close();

            if (!string.IsNullOrEmpty(error) || !good)
                throw new InvalidOperationException(error == "access_denied" ? "Google sign-in was cancelled." : "Google sign-in did not finish.");

            var text = await AssetraHttp.SendAsync("POST", GoogleTokenUrl, ExchangeBody(ClientId, ClientSecret, code, verifier, redirect), "application/x-www-form-urlencoded");
            var idToken = (string)JObject.Parse(text)["id_token"];

            if (string.IsNullOrEmpty(idToken)) throw new InvalidOperationException("Google did not return a sign-in.");
            return idToken;
        }

        static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        // ---- Requests as pure functions (tested without the network) -------------------------------------

        public static string NewVerifier()
        {
            var bytes = new byte[48];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            return Base64Url(bytes);
        }

        /// <summary>PKCE S256: base64url of the SHA-256 of the verifier (RFC 7636).</summary>
        public static string Challenge(string verifier)
        {
            using var sha = SHA256.Create();
            return Base64Url(sha.ComputeHash(Encoding.ASCII.GetBytes(verifier)));
        }

        public static string AuthUrl(string clientId, string redirect, string challenge, string state) =>
            GoogleAuthEndpoint + "?client_id=" + Uri.EscapeDataString(clientId)
            + "&redirect_uri=" + Uri.EscapeDataString(redirect)
            + "&response_type=code&scope=" + Uri.EscapeDataString("openid email profile")
            + "&code_challenge=" + challenge + "&code_challenge_method=S256"
            + "&state=" + Uri.EscapeDataString(state)
            + "&prompt=select_account";

        public static string ExchangeBody(string clientId, string secret, string code, string verifier, string redirect)
        {
            var body = new StringBuilder("grant_type=authorization_code")
                .Append("&code=").Append(Uri.EscapeDataString(code))
                .Append("&client_id=").Append(Uri.EscapeDataString(clientId))
                .Append("&redirect_uri=").Append(Uri.EscapeDataString(redirect))
                .Append("&code_verifier=").Append(Uri.EscapeDataString(verifier));
            if (!string.IsNullOrEmpty(secret)) body.Append("&client_secret=").Append(Uri.EscapeDataString(secret));
            return body.ToString();
        }

        public static string IdpUrl(string apiKey) =>
            "https://identitytoolkit.googleapis.com/v1/accounts:signInWithIdp?key=" + Uri.EscapeDataString(apiKey);

        public static string RefreshUrl(string apiKey) =>
            "https://securetoken.googleapis.com/v1/token?key=" + Uri.EscapeDataString(apiKey);

        public static string IdpBody(string googleIdToken) =>
            new JObject
            {
                ["postBody"] = "id_token=" + Uri.EscapeDataString(googleIdToken) + "&providerId=google.com",
                ["requestUri"] = "http://localhost",
                ["returnSecureToken"] = true,
                ["returnIdpCredential"] = true,
            }.ToString(Newtonsoft.Json.Formatting.None);

        public static string RefreshBody(string refreshToken) =>
            "grant_type=refresh_token&refresh_token=" + Uri.EscapeDataString(refreshToken);

        /// <summary>
        /// Reads the JSON Google lets you download for an OAuth client: <c>{"installed":{"client_id":..,"client_secret":..}}</c>
        /// (or "web", or the two fields alone). Returns false when it has no client id.
        /// </summary>
        public static bool TryReadOauthFile(string json, out string clientId, out string clientSecret)
        {
            clientId = clientSecret = "";

            try
            {
                var root = JObject.Parse(json);
                var node = (JObject)(root["installed"] ?? root["web"] ?? root);
                clientId = (string)node["client_id"] ?? "";
                clientSecret = (string)node["client_secret"] ?? "";
            }
            catch (Exception)
            {
                return false;
            }

            return clientId.Length > 0;
        }

        static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
