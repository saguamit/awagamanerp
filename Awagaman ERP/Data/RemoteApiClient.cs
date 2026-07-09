using System;
using System.Collections.Generic;
using System.Collections;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace Awagaman_ERP.Data
{
    internal static class RemoteApiClient
    {
        private static readonly HttpClient Client = CreateClient();
        private static readonly JavaScriptSerializer Serializer = CreateSerializer();

        private static HttpClient CreateClient()
        {
            var baseUrl = BackendSettings.ApiBaseUrl;
            var client = new HttpClient { BaseAddress = new Uri(baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/") };
            client.Timeout = TimeSpan.FromSeconds(10);
            return client;
        }

        private static JavaScriptSerializer CreateSerializer()
        {
            return new JavaScriptSerializer
            {
                MaxJsonLength = int.MaxValue,
                RecursionLimit = 256
            };
        }

        public static List<T> GetList<T>(string route)
        {
            ApplyAuthHeader();
            var json = Client.GetStringAsync(route).GetAwaiter().GetResult();
            return Serializer.Deserialize<List<T>>(json) ?? new List<T>();
        }

        public static T Get<T>(string route) where T : class
        {
            ApplyAuthHeader();
            var json = Client.GetStringAsync(route).GetAwaiter().GetResult();
            return Serializer.Deserialize<T>(json);
        }

        public static T Post<T>(string route, object body) where T : class
        {
            var json = SendJson(HttpMethod.Post, route, body).GetAwaiter().GetResult();
            return Serializer.Deserialize<T>(json);
        }

        public static RemotePagedResult<T> GetPage<T>(string route)
        {
            ApplyAuthHeader();
            var json = Client.GetStringAsync(route).GetAwaiter().GetResult();
            return Serializer.Deserialize<RemotePagedResult<T>>(json) ?? new RemotePagedResult<T>();
        }

        public static string UrlEncode(string value)
        {
            return Uri.EscapeDataString(value ?? string.Empty);
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
            ApplyAuthHeader();
            Client.DeleteAsync(route).GetAwaiter().GetResult().EnsureSuccessStatusCode();
        }

        public static void PostNoContent(string route)
        {
            ApplyAuthHeader();
            using (var response = Client.PostAsync(route, new StringContent(string.Empty, Encoding.UTF8, "application/json")).GetAwaiter().GetResult())
            {
                response.EnsureSuccessStatusCode();
            }
        }

        private static void ApplyAuthHeader()
        {
            if (string.IsNullOrWhiteSpace(AuthSession.Token))
            {
                Client.DefaultRequestHeaders.Authorization = null;
                return;
            }

            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthSession.Token);
        }

        private static async System.Threading.Tasks.Task<string> SendJson(HttpMethod method, string route, object body)
        {
            ApplyAuthHeader();
            var json = Serializer.Serialize(SanitizeForApi(body));
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

        private static object SanitizeForApi(object value)
        {
            if (value == null)
            {
                return null;
            }

            var type = value.GetType();
            if (type == typeof(string) || type.IsPrimitive || type.IsEnum || type == typeof(decimal))
            {
                return value;
            }

            if (value is DateTime dateTime)
            {
                return FormatBusinessDate(dateTime);
            }

            if (value is DateTimeOffset dateTimeOffset)
            {
                return FormatBusinessDate(dateTimeOffset.DateTime);
            }

            if (value is IDictionary dictionary)
            {
                var normalized = new Dictionary<string, object>();
                foreach (DictionaryEntry entry in dictionary)
                {
                    normalized[Convert.ToString(entry.Key) ?? string.Empty] = SanitizeForApi(entry.Value);
                }
                return normalized;
            }

            if (value is IEnumerable enumerable && !(value is string))
            {
                var normalized = new List<object>();
                foreach (var item in enumerable)
                {
                    normalized.Add(SanitizeForApi(item));
                }
                return normalized;
            }

            var result = new Dictionary<string, object>();
            foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length > 0)
                {
                    continue;
                }

                result[property.Name] = SanitizeForApi(property.GetValue(value, null));
            }

            return result;
        }

        private static string FormatBusinessDate(DateTime value)
        {
            var normalized = value.Kind == DateTimeKind.Utc
                ? value.ToLocalTime()
                : value;
            return DateTime.SpecifyKind(normalized, DateTimeKind.Unspecified).ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        }
    }
}
