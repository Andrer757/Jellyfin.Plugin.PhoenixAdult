using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
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
    public class NetworkPervCity : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var googleResults = await GoogleSearch.Search(searchTitle, siteNum, cancellationToken);
            var searchResults = googleResults.Where(u => u.Contains("trailers") && !u.Contains("as3")).Select(u => u.Replace("www.", ""));

            foreach (var sceneUrl in searchResults)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
                    string subSite = detailsPageElements.SelectSingleNode("//div[@class='about']//h3")?.InnerText.Replace("About", "").Trim();
                    string curId = Helper.Encode(sceneUrl);

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}" } },
                        Name = $"{titleNoFormatting} [PervCity/{subSite}]",
                        SearchProviderName = Plugin.Instance.Name
                    });
                }
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

            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='infoBox clear']/p")?.InnerText.Trim();
            movie.AddStudio("PervCity");

            string tagline = detailsPageElements.SelectSingleNode("//div[@class='about']//h3")?.InnerText.Replace("About", "").Trim();
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            var actorNodes = detailsPageElements.SelectNodes("//h3/span/a");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string modelUrl = actor.GetAttributeValue("href", "").Replace(Helper.GetSearchBaseURL(siteNum).Replace("www.", ""), Helper.GetSearchBaseURL(new[] { 1165, 0 }).Replace("www.", ""));
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(modelUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = await HTML.ElementFromString(actorHttp.Content, cancellationToken);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='starPic']/img")?.GetAttributeValue("src", "");

                        var sceneNodes = actorPage.SelectNodes("//div[@class='videoBlock']");
                        if(sceneNodes != null)
                        {
                            var sceneNode = sceneNodes.FirstOrDefault(s => (s.SelectSingleNode(".//h3")?.InnerText.Replace("...", "").Trim().ToLower() ?? "").Contains(movie.Name.ToLower()));
                            if(sceneNode != null)
                            {
                                var dateNode = sceneNode.SelectSingleNode(".//div[@class='date']");
                                if (dateNode != null && DateTime.TryParse(dateNode.InnerText, out var parsedDate))
                                {
                                    movie.PremiereDate = parsedDate;
                                    movie.ProductionYear = parsedDate.Year;
                                }
                            }
                        }
                    }
                    result.People.Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var imageNodes = detailsPageElements.SelectNodes("//div[@class='snap']");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("src0_3x", "");
                    if (!imageUrl.StartsWith("http"))
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    images.Add(new RemoteImageInfo { Url = imageUrl });
                }
            }

            if (images.Any())
                images.First().Type = ImageType.Primary;

            return images;
        }
    }
}
