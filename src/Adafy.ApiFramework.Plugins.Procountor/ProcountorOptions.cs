namespace Adafy.ApiFramework.Plugins.Procountor
{
    public class ProcountorOptions
    {
        public string ClientId { get; set; } = "";
        public string ClientSecret { get; set; } = "";
        public string RedirectUri { get; set; } = "";
        public string ApiKey { get; set; } =
            "";
        public string Url { get; set; } = "https://api.procountor.com/latest/api";
        public string SpecificationUrl { get; set; } = "https://dev.procountor.com/static/swagger.latest.json";
        public bool IsReadOnly { get; set; } = true;
        public bool IsAutoPagingEnabled { get; set; } = true;
    }
}
