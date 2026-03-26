using System.Text;
using System.Xml;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace MyCustomUmbracoProject.Middleware
{
    public class SitemapMiddleware
    {
        private readonly RequestDelegate _next;

        public SitemapMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(
            HttpContext context,
            IUmbracoContextFactory umbracoContextFactory,
            IContentService contentService)
        {
         

            // Debug endpoint
            if (context.Request.Path.Equals("/sitemap-debug", StringComparison.OrdinalIgnoreCase))
            {
                using var ctx = umbracoContextFactory.EnsureUmbracoContext();
                var cache = ctx.UmbracoContext.Content;
                var sb = new StringBuilder();

                sb.AppendLine($"Cache is null: {cache == null}");

                var rootContent = contentService.GetRootContent().ToList();
                sb.AppendLine($"Root nodes from ContentService: {rootContent.Count}");

                foreach (var r in rootContent)
                {
                    sb.AppendLine($"  - [{r.Id}] {r.Name} (Published: {r.Published})");
                    var published = await cache!.GetByIdAsync(r.Id);
                    sb.AppendLine($"    Published cache hit: {published != null}");
                    if (published != null)
                    {
                        sb.AppendLine($"    Url: {published.Url(mode: UrlMode.Absolute)}");
                        foreach (var child in published.DescendantsOrSelf())
                        {
                            var url = child.Url(mode: UrlMode.Absolute);
                            var excluded = child.Value<bool>("excludeFromSitemap");
                            sb.AppendLine($"    Node [{child.Id}] {child.Name} | Url: {url} | Excluded: {excluded}");
                        }
                    }
                }

                context.Response.ContentType = "text/plain; charset=utf-8";
                await context.Response.WriteAsync(sb.ToString(), Encoding.UTF8);
                return;
            }

            if (!context.Request.Path.Equals("/sitemap.xml", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            using var umbracoContext = umbracoContextFactory.EnsureUmbracoContext();

            var cache2 = umbracoContext.UmbracoContext.Content;
            if (cache2 == null)
            {
                context.Response.StatusCode = 503;
                return;
            }

            var rootNodes = new List<IPublishedContent>();
            foreach (var r in contentService.GetRootContent())
            {
                var published = await cache2.GetByIdAsync(r.Id);
                if (published != null)
                    rootNodes.Add(published);
            }

            var allNodes = rootNodes.SelectMany(n => n.DescendantsOrSelf());
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            var xml = BuildSitemapXml(allNodes, baseUrl);

            context.Response.ContentType = "application/xml; charset=utf-8";
            await context.Response.WriteAsync(xml, Encoding.UTF8);
        }

        private static string BuildSitemapXml(IEnumerable<IPublishedContent> nodes, string baseUrl)
        {
            var sb = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            using (var writer = XmlWriter.Create(sb, settings))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

                foreach (var node in nodes)
                {
                    if (node.Value<bool>("excludeFromSitemap"))
                        continue;

                    var url = node.Url(mode: UrlMode.Absolute);

                    if (string.IsNullOrWhiteSpace(url) || url.StartsWith('#'))
                        continue;

                    if (url.StartsWith('/'))
                        url = baseUrl + url;

                    writer.WriteStartElement("url");
                    writer.WriteElementString("loc", url);
                    writer.WriteElementString("lastmod", node.UpdateDate.ToString("yyyy-MM-dd"));

                    var changeFreq = node.Value<string>("sitemapChangeFreq");
                    writer.WriteElementString("changefreq",
                        string.IsNullOrWhiteSpace(changeFreq) ? GetDefaultChangeFreq(node.ContentType.Alias) : changeFreq);

                    var priority = node.Value<string>("sitemapPriority");
                    writer.WriteElementString("priority",
                        string.IsNullOrWhiteSpace(priority) ? GetDefaultPriority(node.Level) : priority);

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            } // writer is flushed and closed here, before sb.ToString()

            return sb.ToString();
        }

        private static string GetDefaultPriority(int level) => level switch
        {
            1 => "1.0",
            2 => "0.8",
            3 => "0.6",
            _ => "0.4"
        };

        private static string GetDefaultChangeFreq(string contentTypeAlias) => contentTypeAlias switch
        {
            "homePage"     => "daily",
            "newsListPage" => "daily",
            "newsItem"     => "monthly",
            "aboutPage"    => "monthly",
            _              => "weekly"
        };
    }
}
