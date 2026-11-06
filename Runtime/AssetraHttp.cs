using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace WebArt.Assetra
{
    /// <summary>A failed HTTP call: the status code and the answer text, so callers can tell 401 from 404.</summary>
    public class AssetraHttpException : Exception
    {
        public long Status { get; }

        public AssetraHttpException(long status, string message) : base(message)
        {
            Status = status;
        }
    }

    /// <summary>The one place that sends requests, so the client, the sign-in and the downloads behave alike.</summary>
    public static class AssetraHttp
    {
        public static async Task<string> SendAsync(string method, string url, string body, string contentType = "application/json", string bearer = null)
        {
            using var request = new UnityWebRequest(url, method) { downloadHandler = new DownloadHandlerBuffer() };

            if (!string.IsNullOrEmpty(bearer)) request.SetRequestHeader("Authorization", "Bearer " + bearer);

            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", contentType);
            }

            await Send(request);

            if (request.result != UnityWebRequest.Result.Success)
                throw new AssetraHttpException(request.responseCode, $"{method} {url}: HTTP {request.responseCode} {request.downloadHandler?.text ?? request.error}");

            return request.downloadHandler.text;
        }

        public static async Task<byte[]> GetBytesAsync(string url)
        {
            using var request = UnityWebRequest.Get(url);
            await Send(request);

            if (request.result != UnityWebRequest.Result.Success)
                throw new AssetraHttpException(request.responseCode, $"GET {url}: HTTP {request.responseCode} {request.error}");

            return request.downloadHandler.data;
        }

        static Task Send(UnityWebRequest request)
        {
            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.SetResult(true);
            return done.Task;
        }
    }
}
