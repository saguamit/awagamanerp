using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace Awagaman_ERP.Data
{
    internal static class RemoteApiClient
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        private static HttpClient CreateClient()
        {
            var baseUrl = BackendSettings.ApiBaseUrl;
            var client = new HttpClient { BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/") };
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        public static List<T> GetList<T>(string route)
        {
            var json = Client.GetStringAsync(route).GetAwaiter().GetResult();
            return Serializer.Deserialize<List<T>>(json) ?? new List<T>();
        }

        public static T Get<T>(string route) where T : class
        {
            var json = Client.GetStringAsync(route).GetAwaiter().GetResult();
            return Serializer.Deserialize<T>(json);
        }

        public static int PostAndReadInt(string route, object body)
        {
            var responseJson = SendJson(HttpMethod.Post, route, body).GetAwaiter().GetResult();
            return ExtractId(responseJson);
        }

        public static void Put(string route, object body)
        {
            SendJson(HttpMethod.Put, route, body).GetAwaiter().GetResult();
        }

        public static void Delete(string route)
        {
            Client.DeleteAsync(route).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        }

        private static async System.Threading.Tasks.Task<string> SendJson(HttpMethod method, string route, object body)
        {
            var json = Serializer.Serialize(body);
            using (var request = new HttpRequestMessage(method, route))
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        var responseBody = response.Content == null
                            ? string.Empty
                            : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new HttpRequestException(
                            $"Remote API {method.Method} {route} failed: {(int)response.StatusCode} {response.ReasonPhrase}. {responseBody}");
                    }
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        private static int ExtractId(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return 0;
            }

            try
            {
                var dict = Serializer.Deserialize<Dictionary<string, object>>(responseJson);
                if (dict != null && dict.ContainsKey("id"))
                {
                    return Convert.ToInt32(dict["id"]);
                }
            }
            catch
            {
            }

            return 0;
        }
    }
}
