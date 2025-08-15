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
    public class SiteJVRPorn : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            string sceneId = null;
            if (int.TryParse(searchTitle.Split(' ').FirstOrDefault() ?? "", out var id))
            {
                sceneId = id.ToString();
                searchTitle = searchTitle.Replace(sceneId, "").Trim();
            }

            var searchResults = new List<string>();
            if (sceneId != null)
                searchResults.Add($"{Helper.GetSearchSearchURL(siteNum)}{sceneId}");

            if(!string.IsNullOrEmpty(searchTitle))
            {
                var googleResults = await GoogleSearch.Search(searchTitle, siteNum, cancellationToken);
                searchResults.AddRange(googleResults.Where(u => u.Contains("/video/")));
            }

            foreach (var sceneUrl in searchResults.Distinct())
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);
                    string titleNoFormatting = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
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
            string sceneDate = providerIds.Length > 2 ? providerIds[2] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK) return result;
            var detailsPageElements = await HTML.ElementFromString(httpResult.Content, cancellationToken);

            var movie = (Movie)result.Item;
            movie.Name = detailsPageElements.SelectSingleNode("//h1")?.InnerText.Trim();
            movie.Overview = detailsPageElements.SelectSingleNode("//pre")?.InnerText.Trim();
            movie.AddStudio("JVR Porn");
            movie.AddCollection(new[] { "JVR Porn" });

            if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//td[contains(@class, 'tags')]//span");
            if(genreNodes != null)
            {
                foreach(var genre in genreNodes)
                    movie.AddGenre(genre.InnerText.Trim());
            }

            var actorNodes = detailsPageElements.SelectNodes("//a[@class='actress']//span");
            if(actorNodes != null)
            {
                foreach(var actor in actorNodes)
                    result.People.Add(new PersonInfo { Name = actor.InnerText.Trim(), Type = PersonKind.Actor });
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

            var imageNodes = detailsPageElements.SelectNodes("//div[contains(@id, 'snapshot-gallery')]//a | //deo-video");
            if(imageNodes != null)
            {
                foreach(var img in imageNodes)
                {
                    string imageUrl = img.GetAttributeValue("href", "") ?? img.GetAttributeValue("cover-image", "");
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
