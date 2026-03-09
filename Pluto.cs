namespace MediaNight.C_.MediaSources;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Interfaces;
using PuppeteerSharp;
using Tools;


public class Pluto : IMediaSource
{
	static Pluto()
	{
		Pluto.CategoryNamesByInitialSourceURL = new Dictionary<string, string>
		{
			{ "https://pluto.tv/us/on-demand/", string.Empty }
		};

		Pluto.CategoryNamesBySourceURL = new Dictionary<string, string>();
	}

	public static Dictionary<string, string> CategoryNamesByInitialSourceURL { get; set; }
	public static Dictionary<string, string> CategoryNamesBySourceURL { get; set; }


	public static async Task SetCategoryNamesBySourceURLAsync()
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;
		
		ProgressBarManager.IncrementMaximum(1, mediaSourceName);

		await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });

		var pageHtml = await MediaNightTools.GetFinalHTMLViaPuppeteerAsync(
			browser, ".text-holder", mediaSourceName, "super categories", Pluto.CategoryNamesByInitialSourceURL.First().Key
		);

		if (pageHtml is null)
		{
			await browser.CloseAsync();
			return;
		}

		await Pluto.FindAndProcessSuperCategories(pageHtml, browser);

		await browser.CloseAsync();
		ProgressBarManager.IncrementValue(mediaSourceName);
	}


	private static async Task FindAndProcessSuperCategories(string pageHtml, IBrowser browser)
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;
		
		const string indicatorSuperCategoryBlock = "<span class=\"text-holder\">";
		var startPositionsSuperCategoryBlocks = pageHtml.GetAllIndicesOfSubstring(indicatorSuperCategoryBlock);

		ProgressBarManager.IncrementMaximum(startPositionsSuperCategoryBlocks.Count, mediaSourceName);

		foreach (var startPositionSuperCategoryBlock in startPositionsSuperCategoryBlocks)
		{
			const string indicatorSuperCategoryName = "<span class=\"text-holder\">";
			var startPositionName = pageHtml.IndexOf(indicatorSuperCategoryName, startPositionSuperCategoryBlock) +
			    indicatorSuperCategoryName.Length;
			var endPositionName = pageHtml.IndexOf('<', startPositionName + 1);
			var categoryName = pageHtml.Mid(startPositionName, endPositionName - startPositionName);

			const string indicatorSuperCategoryURL = "data-id=\"";
			var startPositionURL = pageHtml.IndexOf(indicatorSuperCategoryURL, startPositionSuperCategoryBlock) +
			    indicatorSuperCategoryURL.Length;
			var endPositionURL = pageHtml.IndexOf('\"', startPositionURL + 1);
			var categoryURL = "https://pluto.tv/us/on-demand/" + pageHtml.Mid(
				startPositionURL, endPositionURL - startPositionURL
			);
			
			await Pluto.SetCategoryNameBySourceURLAsyncUsingInnerCategoriesAsync(categoryURL, browser,
				categoryName);

			ProgressBarManager.IncrementValue(mediaSourceName);
		}
	}


	private static async Task SetCategoryNameBySourceURLAsyncUsingInnerCategoriesAsync(
		string superCategoryURL, IBrowser browser, string superCategoryName
	)
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;

		var pageHtml = await MediaNightTools.GetFinalHTMLViaPuppeteerAsync(
			browser, "h4", mediaSourceName, superCategoryName, superCategoryURL			
		);

		if (pageHtml is null)
		{
			return;
		}
		
		var indicatorCategoryBlock = "<div class=\"category-0-2-265 childCategory\"><h4>";
		var startPositionsCategoryBlocks = pageHtml.GetAllIndicesOfSubstring(indicatorCategoryBlock);
		if (startPositionsCategoryBlocks.Count == 0)
		{
			indicatorCategoryBlock = "<div class=\"category-0-2-266 childCategory\"><h4>";
			startPositionsCategoryBlocks = pageHtml.GetAllIndicesOfSubstring(indicatorCategoryBlock);
		}
		
		ProgressBarManager.IncrementMaximum(startPositionsCategoryBlocks.Count, mediaSourceName);

		foreach (var startPositionCategoryBlock in startPositionsCategoryBlocks)
		{
			ProgressBarManager.SetDisplayedTaskText(
				"Getting " + superCategoryName + " categories from " + mediaSourceName, mediaSourceName
			);
			
			var startPositionName = startPositionCategoryBlock + indicatorCategoryBlock.Length;
			var endPositionName = pageHtml.IndexOf('<', startPositionName + 1);
			var categoryName = pageHtml.Mid(startPositionName, endPositionName - startPositionName);

			const string indicatorCategoryURL = "</h4><span><a href=\"";
			var startPositionURL = pageHtml.IndexOf(indicatorCategoryURL, startPositionCategoryBlock) +
			    indicatorCategoryURL.Length;
			var endPositionURL = pageHtml.IndexOf('\"', startPositionURL + 1);
			var categoryURL = "https://pluto.tv" + pageHtml.Mid(startPositionURL, endPositionURL - startPositionURL);
			
			Pluto.CategoryNamesBySourceURL.TryAdd(categoryURL, categoryName);
			ProgressBarManager.IncrementValue(mediaSourceName);
		}
	}


	public static async Task<HashSet<string>?> GetSearchResultsAndAddNewMediaToViewModelAsync(string sourceURL, string categoryName)
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;

		await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });

		var pageHtml = await MediaNightTools.GetFinalHTMLViaPuppeteerAsync(
			browser, "itemContainer vod-item-poster-atc", mediaSourceName, categoryName, sourceURL			
		);
		
		await browser.CloseAsync();

		if (pageHtml is null)
		{
			return null;
		}

		const string indicator = "itemContainer vod-item-poster-atc\"";
		var startPositions = pageHtml.GetAllIndicesOfSubstring(indicator);

		foreach (var startPositionMediaBlock in startPositions)
		{
			var endPositionMediaBlock = pageHtml.IndexOf("</li>", startPositionMediaBlock);
			var mediaBlock = pageHtml.Mid(startPositionMediaBlock, endPositionMediaBlock - startPositionMediaBlock);

			var title = Pluto.GetTitle(mediaBlock);
			var year = Pluto.GetYear(title);

			if (
				ViewModel.DistinctTitleYears.Contains(title + " - " + year) ||
				ViewModel.IgnoreList.Contains(title + " - " + year) ||
				string.IsNullOrEmpty(title)
			)
			{
				continue;
			}

			var mediaURL = Pluto.GetMediaURL(mediaBlock, -1, title, year);
			var type = Pluto.GetMediaType(mediaURL, title, year);
			if (type.Equals(ViewModel.MediaType.LiveTV))
			{
				continue;
			}

			var posterURL = Pluto.GetPosterURL(mediaBlock, title, year);
			var duration = type.Equals(ViewModel.MediaType.TVShow) ? "? seasons" : "? min";
			var ageRating = mediaURL.Contains("/on-demand/66edc6fe2cf95c0008459ffc/") ?  
				ViewModel.AgeRatings.PG : ViewModel.AgeRatings.Unknown;
			var genre = string.IsNullOrEmpty(categoryName) ? string.Empty : categoryName;

			if (
				!ViewModel.DistinctTitleYears.Contains(title + "-" + year)
			)
			{
				ViewModel.DistinctTitleYears.Add(title + "-" + year);

				var posterGridContainer = await Calculations.GetPosterGridContainerAsync(
					mediaURL, posterURL, mediaSourceName, title, year,
					MediaNightTools.GetMetadataTextForPosterLabel(
						title, type, genre, duration, year, ageRating, mediaSourceName
					)
				);

				lock (ViewModel.LockMedia)
				{
					var row = ViewModel.NewlyAddedMedia.NewRow();
					row["Type"] = type;
					row["Title"] = title;
					row["Year"] = year;
					row["AgeRating"] = ageRating;
					row["Genre"] = categoryName;
					row["Duration"] = duration;
					row["PosterGridContainer"] = posterGridContainer;
					row["MediaURL"] = mediaURL;
					row["Source"] = "Pluto";

					ViewModel.NewlyAddedMedia.Rows.Add(row);
				}
			}
		}

		// Pluto's search is unusuable without an API token - none of these are search results
		return null;
	}


	public static string GetMediaURL(string mediaBlock, int discardForInterface, string title, string year)
	{
		const string indicator = "<a href=\"";
		var startPosition = mediaBlock.IndexOf(indicator) + indicator.Length;
		var endPosition = mediaBlock.IndexOf('\"', startPosition);
		var mediaURLSuffix = mediaBlock.Mid(startPosition, endPosition - startPosition);
		if (ExtensionMethods.Right(mediaURLSuffix, 8).Equals("/details"))
		{
			mediaURLSuffix = ExtensionMethods.Left(mediaURLSuffix, mediaURLSuffix.Length - 7);
		}

		var mediaURL = "https://pluto.tv" + mediaURLSuffix;
		if (string.IsNullOrEmpty(mediaURL))
		{
			GD.PushError("Missing media URL for " + title + "-" + year);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		return mediaURL;
	}


	public static string? GetURLForSearchTerm(string searchTerm)
	{
		// You have to have an API token to request the search URL and get a proper response, e.g. below for "test"
		// https://service-media-search.clusters.pluto.tv/v1/search?q=test&limit=100
		return null;
	}


	private static string? GetTitle(string mediaBlock)
	{
		const string indicator = "<img alt=\"";
		var indicatorPosition = mediaBlock.IndexOf(indicator);
		if (indicatorPosition == -1)
		{
			return null;
		}
		var startPosition = indicatorPosition + indicator.Length;

		var endPosition = mediaBlock.IndexOf('\"', startPosition);
		var title = mediaBlock.Mid(startPosition, endPosition - startPosition)
			.Replace("&amp;", "&");

		return title;
	}


	private static string? GetYear(string? title)
	{
		if (
			string.IsNullOrEmpty(title) ||
			title.Length < 8
		)
		{
			return "-1";
		}

		var potentialYear = ExtensionMethods.Right(title, 7);
		var potentialYearIsNumeric = int.TryParse(potentialYear.Mid(2, 4), out var potentialYearAsInt);
		if (
			potentialYearIsNumeric &&
			potentialYearAsInt >= 1878 &&
			potentialYearAsInt <= DateTime.Today.Year &&
			potentialYear[0].Equals(' ') &&
			potentialYear[1].Equals('(') &&
			potentialYear[6].Equals(')')
		)
		{
			return potentialYear.Mid(2, 4);			
		}
			
		return "-1";
	}


	private static ViewModel.MediaType GetMediaType(string mediaURL, string title, string year)
	{
		var type = ViewModel.MediaType.Unknown;
		if (mediaURL.Contains("/movies/"))
		{
			type = ViewModel.MediaType.Movie;
		}

		if (mediaURL.Contains("/series/"))
		{
			type = ViewModel.MediaType.TVShow;
		}

		if (mediaURL.Contains("/live/"))
		{
			type = ViewModel.MediaType.LiveTV;
		}

		if (type.Equals(ViewModel.MediaType.Unknown))
		{
			GD.PushError("Unrecognized media type for " + title + " - " + year + ": " + mediaURL);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		return type;
	}


	private static string GetPosterURL(string mediaBlock, string title, string year)
	{
		const string indicator = "src=\"";
		var startPosition = mediaBlock.IndexOf(indicator) + indicator.Length;
		var endPosition = mediaBlock.IndexOf('?', startPosition);
		var posterURL = mediaBlock.Mid(startPosition, endPosition - startPosition);
		if (string.IsNullOrEmpty(posterURL))
		{
			GD.PushError("Missing poster URL for " + title + " - " + year);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		return posterURL;
	}
}