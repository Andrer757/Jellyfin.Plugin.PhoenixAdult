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
using Jellyfin.Data.Enums;

namespace PhoenixAdult.Sites
{
    public class NetworkFullPornNetwork : IProviderBase
    {
        public async Task<List<RemoteSearchResult>> Search(int[] siteNum, string searchTitle, DateTime? searchDate, CancellationToken cancellationToken)
        {
            var result = new List<RemoteSearchResult>();
            var modelResultsUrls = new List<string>();
            var searchResultsUrls = new List<string>();
            var googleResults = await WebSearch.GetSearchResults(searchTitle, siteNum, cancellationToken);

            string directSearch = searchTitle.Contains(" ") ? searchTitle.Replace(' ', '-').ToLower() : searchTitle;
            modelResultsUrls.Add($"{Helper.GetSearchBaseURL(siteNum)}/models/{directSearch}.html");

            foreach (var modelResultUrl in googleResults)
            {
                if (!modelResultsUrls.Contains(modelResultUrl) && modelResultUrl.Contains("/models/") && !modelResultUrl.Contains("models_") && !modelResultUrl.Contains("join"))
                {
                    modelResultsUrls.Add(modelResultUrl);
                }
            }

            foreach (var searchResultUrl in googleResults)
            {
                if (!searchResultsUrls.Contains(searchResultUrl) && searchResultUrl.Contains("/trailers/"))
                {
                    searchResultsUrls.Add(searchResultUrl);
                }
            }

            foreach (var sceneUrl in searchResultsUrls)
            {
                var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var detailsPageElements = HTML.ElementFromString(httpResult.Content);
                    var titleNode = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'title_bar')] | //h1[contains(@class, 'trailer_title')] | //meta[@property='og:title']");
                    string rawTitle = titleNode?.Name == "meta" ? titleNode.GetAttributeValue("content", string.Empty) : titleNode?.InnerText;
                    string titleNoFormatting = Helper.ParseTitle(rawTitle?.Trim(), siteNum);
                    string curId = Helper.Encode(sceneUrl);
                    string releaseDate = string.Empty;
                    var dateNode = detailsPageElements.SelectSingleNode("//label[contains(., 'Date Added')]/parent::* | //div[@class='video-info']//p");
                    string dateText = dateNode?.InnerText.Replace("Date Added:", string.Empty).Trim();
                    if (!string.IsNullOrEmpty(dateText) && DateTime.TryParse(dateText, out var parsedDate))
                    {
                        releaseDate = parsedDate.ToString("yyyy-MM-dd");
                    }
                    else if (searchDate.HasValue)
                    {
                        releaseDate = searchDate.Value.ToString("yyyy-MM-dd");
                    }

                    var imageUrl = string.Empty;
                    var posterNode = detailsPageElements.SelectSingleNode("//meta[@property='og:image'] | //video");
                    if (posterNode != null)
                    {
                        imageUrl = posterNode.Name == "meta" ? posterNode.GetAttributeValue("content", string.Empty) : posterNode.GetAttributeValue("poster", string.Empty);
                        if (!string.IsNullOrEmpty(imageUrl) && !imageUrl.StartsWith("http"))
                        {
                            imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                        }
                    }

                    result.Add(new RemoteSearchResult
                    {
                        ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                        Name = $"{titleNoFormatting} [FPN/{Helper.GetSearchSiteName(siteNum)}] {releaseDate}",
                        SearchProviderName = Plugin.Instance.Name,
                        ImageUrl = imageUrl,
                    });
                }
            }

            foreach (var modelUrl in modelResultsUrls)
            {
                var httpResult = await HTTP.Request(modelUrl, HttpMethod.Get, cancellationToken);
                if (httpResult.IsOK)
                {
                    var modelPageElements = HTML.ElementFromString(httpResult.Content);
                    var sceneNodes = modelPageElements.SelectNodes("//a[contains(@class, 'swimlane-scene-thumbnail-title-link')] | //div[contains(@class, 'latest-updates')]//div[@data-setid]");
                    if (sceneNodes != null)
                    {
                        foreach (var sceneNode in sceneNodes)
                        {
                            string sceneLink = sceneNode.GetAttributeValue("href", string.Empty);
                            string sceneTitle = sceneNode.InnerText.Trim();
                            if (string.IsNullOrEmpty(sceneLink))
                            {
                                sceneLink = sceneNode.SelectSingleNode(".//a[@class='updateimg']")?.GetAttributeValue("href", string.Empty);
                                sceneTitle = sceneNode.InnerText.Split(':').Last().Trim();
                            }

                            if (!string.IsNullOrEmpty(sceneLink) && !searchResultsUrls.Contains(sceneLink))
                            {
                                string titleNoFormatting = Helper.ParseTitle(sceneTitle, siteNum);
                                string curId = Helper.Encode(sceneLink);
                                string releaseDate = searchDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                                result.Add(new RemoteSearchResult
                                {
                                    ProviderIds = { { Plugin.Instance.Name, $"{curId}|{releaseDate}" } },
                                    Name = $"{titleNoFormatting} [FPN/{Helper.GetSearchSiteName(siteNum)}]",
                                    SearchProviderName = Plugin.Instance.Name,
                                });
                            }
                        }
                    }
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
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            string sceneDate = providerIds.Length > 1 ? providerIds[1] : null;

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return result;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var movie = (Movie)result.Item;
            movie.ExternalId = sceneUrl;

            var titleNode = detailsPageElements.SelectSingleNode("//h1[contains(@class, 'title_bar')] | //h1[contains(@class, 'trailer_title')] | //meta[@property='og:title']");
            string rawTitle = titleNode?.Name == "meta" ? titleNode.GetAttributeValue("content", string.Empty) : titleNode?.InnerText;
            movie.Name = Helper.ParseTitle(rawTitle?.Trim(), siteNum);

            var overviewNode = detailsPageElements.SelectSingleNode("//p[@id='description'] | //div[contains(@class, 'video-description')]/p[@class='description-text'] | //meta[@name='description']");
            string rawOverview = overviewNode?.Name == "meta" ? overviewNode.GetAttributeValue("content", string.Empty) : overviewNode?.InnerText;
            movie.Overview = rawOverview?.Trim();

            movie.AddStudio("Full Porn Network");

            string tagline = Helper.GetSearchSiteName(siteNum);
            movie.AddStudio(tagline);

            var dateNode = detailsPageElements.SelectSingleNode("//label[contains(., 'Date Added')]/parent::* | //div[@class='video-info']//p");
            string dateText = dateNode?.InnerText.Replace("Date Added:", string.Empty).Trim();
            if (!string.IsNullOrEmpty(dateText) && DateTime.TryParse(dateText, out var parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }
            else if (!string.IsNullOrEmpty(sceneDate) && DateTime.TryParse(sceneDate, out parsedDate))
            {
                movie.PremiereDate = parsedDate;
                movie.ProductionYear = parsedDate.Year;
            }

            var genreNodes = detailsPageElements.SelectNodes("//div[@id='preview']//a[contains(@href, '/porn-categories/') or contains(@href, '/categories/')] | //div[contains(@class, 'video-info')]//a[contains(@href, '/categories/')] | //div[contains(@class, 'video-content')]//a[contains(@href, '/porn-categories/') or contains(@href, '/categories/')]");
            if (genreNodes != null)
            {
                foreach (var genre in genreNodes)
                {
                    string genreName = genre.InnerText.Trim();
                    if (!string.IsNullOrEmpty(genreName) && !genreName.Equals("Categories", StringComparison.OrdinalIgnoreCase))
                    {
                        movie.AddGenre(genreName);
                    }
                }
            }

            var actorNodes = detailsPageElements.SelectNodes("//div[@id='preview']//a[contains(@href, '/models/')] | //div[contains(@class, 'video-info')]//a[contains(@href, '/models/')] | //div[contains(@class, 'video-content')]//a[contains(@href, '/models/')]");
            if (actorNodes != null)
            {
                foreach (var actor in actorNodes)
                {
                    string actorName = actor.InnerText.Trim();
                    string actorLink = actor.GetAttributeValue("href", string.Empty);
                    if (string.IsNullOrEmpty(actorName) || actorName.Equals("Models", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!actorLink.StartsWith("http"))
                    {
                        actorLink = Helper.GetSearchBaseURL(siteNum) + actorLink;
                    }

                    var actorHttp = await HTTP.Request(actorLink, HttpMethod.Get, cancellationToken);
                    string actorPhotoUrl = string.Empty;
                    if (actorHttp.IsOK)
                    {
                        var actorPage = HTML.ElementFromString(actorHttp.Content);
                        var actorImgNode = actorPage.SelectSingleNode("//img[contains(@class, 'model_bio_thumb')] | //meta[@property='og:image'] | //img[@alt='model']");
                        if (actorImgNode != null)
                        {
                            if (actorImgNode.Name == "meta")
                            {
                                actorPhotoUrl = actorImgNode.GetAttributeValue("content", string.Empty);
                            }
                            else
                            {
                                actorPhotoUrl = actorImgNode.GetAttributeValue("src", string.Empty);
                                if (string.IsNullOrEmpty(actorPhotoUrl))
                                {
                                    actorPhotoUrl = actorImgNode.GetAttributeValue("data-src", string.Empty);
                                }
                                if (string.IsNullOrEmpty(actorPhotoUrl))
                                {
                                    actorPhotoUrl = actorImgNode.GetAttributeValue("src0_3x", string.Empty);
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(actorPhotoUrl) && !actorPhotoUrl.StartsWith("http"))
                        {
                            actorPhotoUrl = Helper.GetSearchBaseURL(siteNum) + actorPhotoUrl;
                        }
                    }

                    ((List<PersonInfo>)result.People).Add(new PersonInfo { Name = actorName, Type = PersonKind.Actor, ImageUrl = actorPhotoUrl });
                }
            }

            return result;
        }

        public async Task<IEnumerable<RemoteImageInfo>> GetImages(int[] siteNum, string[] sceneID, BaseItem item, CancellationToken cancellationToken)
        {
            var images = new List<RemoteImageInfo>();
            string sceneUrl = Helper.Decode(sceneID[0].Split('|')[0]);
            if (!sceneUrl.StartsWith("http"))
            {
                sceneUrl = Helper.GetSearchBaseURL(siteNum) + sceneUrl;
            }

            var httpResult = await HTTP.Request(sceneUrl, HttpMethod.Get, cancellationToken);
            if (!httpResult.IsOK)
            {
                return images;
            }

            var detailsPageElements = HTML.ElementFromString(httpResult.Content);

            var posterNode = detailsPageElements.SelectSingleNode("//meta[@property='og:image'] | //video");
            if (posterNode != null)
            {
                string imageUrl = posterNode.Name == "meta" ? posterNode.GetAttributeValue("content", string.Empty) : posterNode.GetAttributeValue("poster", string.Empty);
                if (!string.IsNullOrEmpty(imageUrl))
                {
                    if (!imageUrl.StartsWith("http"))
                    {
                        imageUrl = Helper.GetSearchBaseURL(siteNum) + imageUrl;
                    }

                    if (!imageUrl.Contains("token=") && !imageUrl.Contains("expires="))
                    {
                        imageUrl = imageUrl.Replace("-1x.jpg", "-3x.jpg");
                    }

                    images.Add(new RemoteImageInfo { Url = imageUrl, Type = ImageType.Primary });
                }
            }

            return images;
        }
    }
}
