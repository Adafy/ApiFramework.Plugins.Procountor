using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.ReverseProxy.Service.RuntimeModel.Transforms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Weikio.ApiFramework.Abstractions;
using Weikio.ApiFramework.Plugins.OpenApi;
using Weikio.ApiFramework.Plugins.OpenApi.Proxy;
using Weikio.ApiFramework.SDK;
using Endpoint = Weikio.ApiFramework.Abstractions.Endpoint;
using JsonSerializer = Newtonsoft.Json.JsonSerializer;

namespace Adafy.ApiFramework.Plugins.Procountor
{
    public class ProcountorProxy : IEndpointMetadataExtender
    {
        private readonly ILogger<ProcountorProxy> _logger;
        private readonly ILogger<OpenApiProxy> _proxyLogger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IServiceProvider _serviceProvider;
        private readonly IEndpointRouteTemplateProvider _endpointRouteTemplateProvider;

        public ProcountorOptions Configuration { get; set; }

        public ProcountorProxy(ILogger<ProcountorProxy> logger, ILogger<OpenApiProxy> proxyLogger, ILoggerFactory loggerFactory,
            IHttpContextAccessor httpContextAccessor,
            IServiceProvider serviceProvider, IEndpointRouteTemplateProvider endpointRouteTemplateProvider)
        {
            _logger = logger;
            _proxyLogger = proxyLogger;
            _loggerFactory = loggerFactory;
            _httpContextAccessor = httpContextAccessor;
            _serviceProvider = serviceProvider;
            _endpointRouteTemplateProvider = endpointRouteTemplateProvider;
        }

        [FixedHttpConventions]
        [ApiExplorerSettings(IgnoreApi = true)]
        [Route("{**catchAll}")]
        [HttpGet]
        [HttpPost]
        [HttpPut]
        [HttpDelete]
        public async Task Run(string catchAll)
        {
            var httpContext = _httpContextAccessor.HttpContext;

            var apiKey = Configuration.ApiKey;

            if (httpContext.Request.Query.ContainsKey("apikey"))
            {
                apiKey = httpContext.Request.Query["apikey"];
            }

            if (httpContext.Request.Headers.ContainsKey("apikey"))
            {
                apiKey = httpContext.Request.Headers["apikey"];
            }

            var apiOptions = CreateApiOptions(Configuration, apiKey);

            var proxy = new OpenApiProxy(_proxyLogger, _loggerFactory, _httpContextAccessor, _serviceProvider, apiOptions);

            if (Configuration.IsAutoPagingEnabled == false)
            {
                await proxy.RunRequest(catchAll);

                return;
            }

            if (!string.Equals(httpContext.Request.Method, HttpMethods.Get, StringComparison.InvariantCultureIgnoreCase))
            {
                // No need to page unless we are doing GET
                await proxy.RunRequest(catchAll);

                return;
            }
            
            if (httpContext.Request.Query.ContainsKey("size"))
            {
                // No need to auto page as we requested specific amount of items
                await proxy.RunRequest(catchAll);

                return;
            }

            await HandlePaging(catchAll, httpContext, apiOptions, apiKey, proxy);
        }

        private async Task HandlePaging(string catchAll, HttpContext httpContext, ApiOptions apiOptions, string apiKey, OpenApiProxy proxy)
        {
            httpContext.Request.EnableBuffering();
            var originalStream = httpContext.Response.Body;

            try
            {
                var requiresPaging = false;
                var resultIsPaged = false;

                var mergedPages = new JObject();

                var currentPage = 0;

                do
                {
                    using (var newStream = new MemoryStream())
                    {
                        httpContext.Response.Body = newStream;
                        httpContext.Response.Headers.Clear(); 

                        var openApiOptions = apiOptions;

                        if (currentPage > 0)
                        {
                            openApiOptions = CreateApiOptions(Configuration, apiKey, currentPage);
                        }

                        await proxy.RunRequest(catchAll, openApiOptions).ConfigureAwait(false);

                        newStream.Position = 0;

                        using (var jsonReader = new JsonTextReader(new StreamReader(newStream, Encoding.UTF8, true, 8192, true)))
                        {
                            var jToken = await JToken.LoadAsync(jsonReader);

                            var res = jToken;

                            if (jToken is JArray)
                            {
                                // Return results if the endpoint only returns array without any metadata
                                await HandleResult(httpContext, res, originalStream);

                                return;
                            }
                            
                            var supportsPaging = !string.IsNullOrWhiteSpace(res["meta"]?["pageNumber"]?.Value<string>());

                            if (!supportsPaging)
                            {
                                requiresPaging = false;

                                // Return results if the endpoint doesn't support paging
                                await HandleResult(httpContext, res, originalStream);

                                return;
                            }
                            else
                            {
                                currentPage = int.Parse(res["meta"]["pageNumber"].Value<string>());

                                var hasMorePages = !string.Equals(res["meta"]?["resultCount"]?.Value<string>(), "0",
                                    StringComparison.InvariantCultureIgnoreCase);

                                {
                                    if (hasMorePages)
                                    {
                                        requiresPaging = true;
                                    }
                                    else
                                    {
                                        requiresPaging = false;
                                    }
                                }
                            }

                            mergedPages.Merge(res,
                                new JsonMergeSettings()
                                {
                                    MergeArrayHandling = MergeArrayHandling.Concat, MergeNullValueHandling = MergeNullValueHandling.Merge
                                });
                        }

                        if (requiresPaging)
                        {
                            resultIsPaged = true;
                            currentPage += 1;
                        }
                    }
                } while (requiresPaging);

                if (resultIsPaged)
                {
                    if (!string.IsNullOrWhiteSpace(mergedPages["meta"]?["pageNumber"].Value<string>()))
                    {
                        ((JObject) mergedPages["meta"]).Remove("pageNumber");
                        mergedPages["meta"]["pageCount"] = currentPage;
                    }
                    
                    if (!string.IsNullOrWhiteSpace(mergedPages["meta"]?["pageSize"].Value<string>()))
                    {
                        ((JObject) mergedPages["meta"]).Remove("pageSize");
                    }

                    if (!string.IsNullOrWhiteSpace(mergedPages["meta"]?["resultCount"].Value<string>()))
                    {
                        mergedPages["meta"]["resultCount"] = ((JArray) mergedPages["results"]).Count;
                    }
                }

                await HandleResult(httpContext, mergedPages, originalStream);
            }
            finally
            {
                httpContext.Response.Body = originalStream;
            }
        }

        private static async Task HandleResult(HttpContext httpContext, JToken json, Stream originalStream)
        {
            using (var resultStream = new MemoryStream())
            {
                using (var sw = new StreamWriter(resultStream, leaveOpen: true))
                {
                    using (var writer = new JsonTextWriter(sw))
                    {
                        await json.WriteToAsync(writer).ConfigureAwait(false);
                    }

                    resultStream.Seek(0, SeekOrigin.Begin);
                    httpContext.Response.ContentLength = resultStream.Length;

                    await resultStream.CopyToAsync(originalStream).ConfigureAwait(false);
                }
            }
        }

        [ApiExplorerSettings(IgnoreApi = true)]
        [NonAction]
        [FixedHttpConventions]
        public async Task<List<object>> GetMetadata(Endpoint endpoint)
        {
            var nswagExtender = new NSwagMetadataExtender(_endpointRouteTemplateProvider);

            var endpointConfiguration = GetConfiguration(endpoint);
            var apiOptions = CreateApiOptions(endpointConfiguration, endpointConfiguration.ApiKey);

            return await nswagExtender.GetMetadata(endpoint, apiOptions);
        }

        private ProcountorOptions GetConfiguration(Endpoint endpoint)
        {
            if (endpoint.Configuration is ProcountorOptions options)
            {
                return options;
            }

            var result = JsonConvert.DeserializeObject<ProcountorOptions>(JsonConvert.SerializeObject(endpoint.Configuration));

            return result;
        }

        private static ApiOptions CreateApiOptions(ProcountorOptions procountorOptions, string apiKey, int page = 0)
        {
            var apiOptions = new ApiOptions()
            {
                SpecificationUrl = procountorOptions.SpecificationUrl,
                ApiUrl = procountorOptions.Url,
                BeforeRequest = async context =>
                {
                    var token = await TokenService.GetToken(procountorOptions, apiKey);

                    return token;
                },
                ConfigureAdditionalHeaders = (context, state) => new Dictionary<string, string> { { "Authorization", "Bearer " + state } },
                Mode = ApiMode.Proxy,
                IncludeOperation = (operationId, operation, config) =>
                {
                    if (procountorOptions.IsReadOnly == false)
                    {
                        return true;
                    }

                    if (string.Equals(operationId, "get", StringComparison.InvariantCultureIgnoreCase))
                    {
                        return true;
                    }

                    return false;
                },
                TagTransformMode = TagTransformModeEnum.UseOriginal,
                PrefixMode = PrefixMode.OnlyPrefix
            };

            if (page > 0)
            {
                apiOptions.ConfigureRequestParameterTransforms = (context, state, currentTransforms) =>
                {
                    var result = new List<RequestParametersTransform>(currentTransforms)
                    {
                        new QueryParameterFromStaticTransform(QueryStringTransformMode.Set, "page", page.ToString())
                    };

                    return result;
                };
            }

            return apiOptions;
        }
    }
}
