using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Adafy.ApiFramework.Plugins.Procountor
{
    public static class TokenService
    {
        private static ConcurrentDictionary<string, (DateTime, string)> _dictionary = new ConcurrentDictionary<string, (DateTime, string)>();
        private static (DateTime?, HttpClient) _client;
        private static string _clientLock = "lock";

        public static async Task<string> GetToken(ProcountorOptions options, string apiKey)
        {
            if (_dictionary.ContainsKey(apiKey))
            {
                var keyAndExpiration = _dictionary[apiKey];

                if (DateTime.Now < keyAndExpiration.Item1)
                {
                    return keyAndExpiration.Item2;
                }
            }

            lock (_clientLock)
            {
                if (_dictionary.ContainsKey(apiKey))
                {
                    _dictionary.Remove(apiKey, out _);
                }
            }

            var accessParams = new Dictionary<string, string>
            {
                { "grant_type", "client_credentials" },
                { "client_id", options.ClientId },
                { "client_secret", options.ClientSecret },
                { "redirect_uri", options.RedirectUri },
                { "api_key", apiKey }
            };

            var accessUrl = $"{options.Url}/oauth/token";

            var accessContent = new FormUrlEncodedContent(accessParams);

            var accessRequest = new HttpRequestMessage()
            {
                RequestUri = new Uri(accessUrl, UriKind.Absolute), Method = HttpMethod.Post, Content = accessContent
            };

            var client = GetHttpClient();

            var resAccess = await client.SendAsync(accessRequest);

            var s = await resAccess.Content.ReadAsStringAsync();

            var obj = JObject.Parse(s);

            var accessToken = obj["access_token"].Value<string>();

            var res = _dictionary.GetOrAdd(apiKey, s1 => (DateTime.Now.AddMinutes(50), accessToken));

            return res.Item2;
        }

        private static HttpClient GetHttpClient()
        {
            if (DateTime.Now - _client.Item1.GetValueOrDefault() < TimeSpan.FromDays(1))
            {
                return _client.Item2;
            }

            lock (_clientLock)
            {
                if (DateTime.Now - _client.Item1.GetValueOrDefault() < TimeSpan.FromDays(1))
                {
                    return _client.Item2;
                }

                var httpClientHandler = new HttpClientHandler { AllowAutoRedirect = false };
                var result = new HttpClient(httpClientHandler);

                _client = (DateTime.Now, result);

                return result;
            }
        }
    }
}
