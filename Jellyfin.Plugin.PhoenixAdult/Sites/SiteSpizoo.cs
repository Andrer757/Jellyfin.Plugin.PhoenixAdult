using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using PhoenixAdult.Extensions;
using PhoenixAdult.Helpers;
using PhoenixAdult.Helpers.Utils;

#if __EMBY__
#else
using Jellyfin.Data.Enums;
#endif

namespace PhoenixAdult.Sites
{
    public class SiteSpizoo : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var searchUrl = $"{Helper.GetSearchSearchURL(siteNum)}{searchTitle.Replace(" ", "+")}";
            var http = await HTTP.Request(searchUrl, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            ParseSearchResults(doc, result);

            var pager = doc.DocumentNode.SelectSingleNode(@"//ul[@class='pager']");
            if (pager != null)
            {
                var pageUrls = new List<string>();
                var pageNodes = pager.SelectNodes(@".//a");
                if (pageNodes != null)
                {
                    foreach (var pageNode in pageNodes)
                    {
                        var pageUrl = pageNode.GetAttributeValue("href", string.Empty);
                        if (!string.IsNullOrEmpty(pageUrl) && !pageUrls.Contains(pageUrl) && pageUrl != "#")
                        {
                            pageUrls.Add(pageUrl);
                        }
                    }
                }

                foreach (var pageUrl in pageUrls)
                {
                    var nextPageHttp = await HTTP.Request($"{Helper.GetSearchBaseURL(siteNum)}/{pageUrl}", cancellationToken);
                    if (nextPageHttp.IsOK)
                    {
                        var nextPageDoc = new HtmlDocument();
                        nextPageDoc.LoadHtml(nextPageHttp.Content);
                        ParseSearchResults(nextPageDoc, result);
                    }
                }
            }

            return result;
        }

        private void ParseSearchResults(HtmlDocument doc, List<RemoteSearchResult> result)
        {
            var searchResults = doc.DocumentNode.SelectNodes(@"//div[@class='model-update row']");
            if (searchResults == null)
            {
                return;
            }

            foreach (var searchResult in searchResults)
            {
                var titleNode = searchResult.SelectSingleNode(@".//h3[@class='titular']");
                var title = titleNode?.InnerText.Trim();

                var urlNode = searchResult.SelectSingleNode(@".//a");
                var sceneUrl = urlNode?.GetAttributeValue("href", string.Empty);

                if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(sceneUrl))
                {
                    continue;
                }

                var dateNode = searchResult.SelectSingleNode(@".//div[@class='date-label']");
                var date = string.Empty;
                if (dateNode != null)
                {
                    var dateText = dateNode.InnerText.Replace("Released date:", "").Trim();
                    if (DateTime.TryParse(dateText, out var parsedDate))
                    {
                        date = parsedDate.ToString("yyyy-MM-dd");
                    }
                }

                result.Add(new RemoteSearchResult
                {
                    ProviderIds = { { Plugin.Instance.Name, Helper.Encode(sceneUrl) } },
                    Name = $"{title} [{date}]",
                    SearchProviderName = Plugin.Instance.Name,
                });
            }

            return result;
        }

        public async Task<MetadataResult<BaseItem>> Update(int[] siteNum, string[] sceneID, CancellationToken cancellationToken)
        {
            var result = new MetadataResult<BaseItem>()
            {
                Item = new Movie(),
                People = new List<PersonInfo>(),
            };
            var movie = (Movie)result.Item;
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (!http.IsOK)
            {
                return result;
            }

            var doc = new HtmlDocument();
            doc.LoadHtml(http.Content);

            movie.Name = doc.DocumentNode.SelectSingleNode(@"//h1")?.InnerText.Trim();
            movie.Overview = doc.DocumentNode.SelectSingleNode(@"//p[@class=""description""] | //p[@class=""description-scene""] | //h2/following-sibling::p")?.InnerText.Trim();
            movie.AddStudio("Spizoo");

            var dateNode = doc.DocumentNode.SelectSingleNode(@"//p[@class=""date""]");
            if (dateNode != null && DateTime.TryParse(dateNode.InnerText.Trim(), out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            // Actor and Genre logic needs to be manually added for each site
            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            // Simplified image logic, may need adjustments
            var images = new List<RemoteImageInfo>();
            var sceneURL = Helper.Decode(sceneID[0]);
            var http = await HTTP.Request(sceneURL, cancellationToken);
            if (http.IsOK)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(http.Content);
                var imageNodes = doc.DocumentNode.SelectNodes("//img/@src");
                if (imageNodes != null)
                {
                    foreach (var img in imageNodes)
                    {
                        var imgUrl = img.GetAttributeValue("src", string.Empty);
                        if (!imgUrl.StartsWith("http"))
                        {
                            imgUrl = new Uri(new Uri(Helper.GetSearchBaseURL(siteNum)), imgUrl).ToString();
                        }

                        images.Add(new RemoteImageInfo { Url = imgUrl });
                    }
                }
            }

            return images;
        }
    }
}
