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
    public class SiteBoundHoneys : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string searchUrl = Helper.GetSearchSearchURL(siteNum) + Uri.EscapeDataString(searchTitle);
            var httpResult = await HTTP.Request(searchUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;

            var searchPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
            var searchNodes = searchPageElements.SelectNodes("//div[@class='update']");
            if (searchNodes != null)
            {
                foreach (var node in searchNodes)
                {
                    var titleNode = node.SelectSingleNode(".//div[@class='updateTitle']");
                    string titleNoFormatting = titleNode?.InnerText.Trim();
                    string sceneUrl = node.SelectSingleNode(".//div[@class='updateTitle']/a")?.GetAttributeValue("href", "");
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? "";

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{siteNum[0]}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [{Helper.GetSearchSiteName(siteNum)}]",
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

            string[] providerIds = sceneID[0].Split('|');
            string sceneUrl = Helper.Decode(providerIds[0]);
            if (!sceneUrl.StartsWith("http"))
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            string releaseDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//div[@class='updateVideoTitle']")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//div[@class='updateDescription']/b")?.InnerText.Trim();
            movie.AddStudio("Bound Honeys");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddTag(tagline);
            movie.AddCollection(new[] { tagline });

            if (!string.IsNullOrEmpty(releaseDate) && DateTime.TryParse(releaseDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@class='updateCategoriesList']/a");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@class='updateModelsList']/a");
            if (actorNodes != null)
            {
                if (actorNodes.Count == 3) movie.AddGenre("Threesome");
                if (actorNodes.Count == 4) movie.AddGenre("Foursome");
                if (actorNodes.Count > 4) movie.AddGenre("Orgy");

                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorPageUrl = Helper.GetSearchBaseURL(siteNum) + actor.GetAttributeValue("href", "");
                    string actorPhotoUrl = string.Empty;
                    var actorHttp = await HTTP.Request(actorPageUrl, HttpMethod.Get, cancellationToken);
                    if(actorHttp.IsOK)
                    {
                        var actorPage = await HTML.ElementFromString(actorHttp.Content, cancellationToken);
                        actorPhotoUrl = actorPage.SelectSingleNode("//div[@class='modelDetailPhoto']/img")?.GetAttributeValue("src", "");
                        if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
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
            if (!sceneUrl.StartsWith("http"))
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return images;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var imageNodes = detailsPageElements.SelectNodes("//link[@rel='preload']");
            if (imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("href", "");
                    if (!imageUrl.StartsWith("http"))
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
