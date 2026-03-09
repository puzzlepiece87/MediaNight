namespace MediaNight.C_.MediaSources;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Interfaces;
using PuppeteerSharp;
using Tools;
using HttpClient = System.Net.Http.HttpClient;


public class Fandango : IMediaSource
{
	static Fandango()
	{
		Fandango.CategoryNamesByInitialSourceURL = new Dictionary<string, string>
		{
			{ "https://athome.fandango.com/content/browse/free/", string.Empty }
		};

		Fandango.CategoryNamesBySourceURL = new Dictionary<string, string>();
	}


	public static Dictionary<string, string> CategoryNamesByInitialSourceURL { get; set; }
	public static Dictionary<string, string> CategoryNamesBySourceURL { get; set; }


	public static async Task SetCategoryNamesBySourceURLAsync()
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;
		
		ProgressBarManager.IncrementMaximum(Fandango.CategoryNamesByInitialSourceURL.Count, mediaSourceName);

		using var httpClient = new HttpClient();
		foreach (var (uRL, category) in Fandango.CategoryNamesByInitialSourceURL)
		{
			Fandango.CategoryNamesBySourceURL.Add(uRL, category);

			ProgressBarManager.SetDisplayedTaskText("Retrieving categories from " + uRL, mediaSourceName);
			var pageHtml = await httpClient.GetStringAsync(uRL);

			const string indicatorCategoryBlock = "<div class=\"col-xs-10 nr-p-0 sectionContainer__sectionTitleLeft--ZNYPp sectionContainer__sectionTitle--XuwKO\">";
			var startPositions = pageHtml.GetAllIndicesOfSubstring(indicatorCategoryBlock);

			foreach (var startPositionCategoryBlock in startPositions)
			{
				var endPosition = pageHtml.IndexOf("</div>", startPositionCategoryBlock);
				var categoryBlock = pageHtml.Mid(startPositionCategoryBlock, endPosition - startPositionCategoryBlock);
				var startPosition = categoryBlock.IndexOf(" href=\"") + 7;
				endPosition = categoryBlock.IndexOf('\"', startPosition);
				var urlSuffix = categoryBlock.Mid(startPosition, endPosition - startPosition);
				const string indicatorName = " aria-label=\"";
				startPosition = categoryBlock.IndexOf(indicatorName);
				string name;
				startPosition += indicatorName.Length;
				endPosition = categoryBlock.IndexOf('\"', startPosition);
				name = categoryBlock.Mid(startPosition, endPosition - startPosition);

				name = Fandango.GetFixedCategoryName(name);

				Fandango.CategoryNamesBySourceURL.TryAdd("https://Fandangotv.com/" + urlSuffix, name);
			}

			ProgressBarManager.IncrementValue(mediaSourceName);
		}
	}


	private static string GetFixedCategoryName(string categoryName)
	{
		categoryName = categoryName.Replace("_", " ");
		categoryName = categoryName.Replace("&amp;", "&");
		categoryName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(categoryName);
		categoryName = categoryName.Replace("Tv", "TV");
		categoryName = categoryName.Replace(" And ", " and ");
		categoryName = categoryName.Replace(" Of ", " of ");
		categoryName = categoryName.Replace("Free | ", string.Empty);
		categoryName = categoryName.Replace(" | Watch for Free", string.Empty);
		categoryName = categoryName.Replace(" | Best Free", string.Empty);
		
		return categoryName;
	}


	public static async Task<HashSet<string>?> GetSearchResultsAndAddNewMediaToViewModelAsync(string sourceURL, string categoryName)
	{
		var searchResults = new HashSet<string>();
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;

		var pageHtml = string.Empty;
		if (ViewModel.MoreSpeedFewerPosters && !sourceURL.Contains("https://Fandangotv.com/search/"))
		{
			using var httpClient = new HttpClient();
			pageHtml = await httpClient.GetStringAsync(sourceURL);
		}
		else
		{
			await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions { Headless = true });
			pageHtml = await MediaNightTools.GetFinalHTMLViaPuppeteerAsync(
				browser, ".web-poster__image-element", mediaSourceName, categoryName, sourceURL
			);

			await browser.CloseAsync();

			if (pageHtml is null)
			{
				return null;
			}
		}

		const string indicator = "<div class=\"web-content-tile__container\">";
		var startPositions = pageHtml.GetAllIndicesOfSubstring(indicator);

		foreach (var startPositionMediaBlock in startPositions)
		{
			var title = Fandango.GetTitle(pageHtml, startPositionMediaBlock);
			var year = Fandango.GetYear(pageHtml, startPositionMediaBlock, title);

			if (
				ViewModel.DistinctTitleYears.Contains(title + " - " + year) ||
				ViewModel.IgnoreList.Contains(title + " - " + year)
			)
			{
				continue;
			}

			var mediaURL = Fandango.GetMediaURL(pageHtml, startPositionMediaBlock, title, year);
			var type = Fandango.GetMediaType(mediaURL, title, year);

			if (type.Equals(ViewModel.MediaType.LiveTV))
			{
				continue;
			}

			var (posterURL, startPosition) =
				Fandango.GetPosterURLAndStartPosition(pageHtml, startPositionMediaBlock, title, year);
			(var duration, startPosition) =
				Fandango.GetDurationAndUpdatedStartPosition(type, startPosition, pageHtml, title, year);
			(var ageRating, startPosition) =
				Fandango.GetAgeRatingAndUpdatedStartPosition(type, startPosition, pageHtml, title, year);
			var genre = Fandango.GetGenre(startPosition, pageHtml, title, year, categoryName);

			if (
				ageRating <= ViewModel.MaxAgeRating
			)
			{
				if (sourceURL.Contains("https://Fandangotv.com/search/"))
				{
					searchResults.Add(string.Join("|", title, year, mediaSourceName));
				}
				
				if (ViewModel.DistinctTitleYears.Contains(title + "-" + year))
				{
					continue;
				}
				
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
					row["Genre"] = genre;
					row["Duration"] = duration;
					row["PosterGridContainer"] = posterGridContainer;
					row["MediaURL"] = mediaURL;
					row["Source"] = mediaSourceName;

					ViewModel.NewlyAddedMedia.Rows.Add(row);
				}
			}
		}
		
		return searchResults.Count > 0 ? searchResults : null;
	}


	public static string GetMediaURL(string pageHtml, int startPositionMediaBlock, string title, string year)
	{
		const string indicator = "<a class=\"web-content-tile__title\" href=\"";
		var startPosition = pageHtml.IndexOf(indicator, startPositionMediaBlock) + indicator.Length;
		var endPosition = pageHtml.IndexOf('\"', startPosition);
		var mediaURLSuffix = pageHtml.Mid(startPosition, endPosition - startPosition);
		var mediaURL = "https://Fandangotv.com" + mediaURLSuffix;
		if (string.IsNullOrEmpty(mediaURL))
		{
			GD.PushError("Missing media URL for " + title + "-" + year);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		return mediaURL;
	}


	public static string? GetURLForSearchTerm(string searchTerm)
	{
		return "https://Fandangotv.com/search/" + searchTerm;
		// Fandango search scrapes don't return any useful media information
		// return null;
	}


	private static string GetTitle(string pageHtml, int startPositionMediaBlock)
	{
		var indicator = "<a class=\"web-content-tile__title\"";
		var startPosition = pageHtml.IndexOf('>', pageHtml.IndexOf(indicator, startPositionMediaBlock)) + 1;
		var endPosition = pageHtml.IndexOf('<', startPosition);
		var title = pageHtml.Mid(startPosition, endPosition - startPosition)
			.Replace("&#x27;", "'")
			.Replace("&amp;", "&");
		if (string.IsNullOrEmpty(title))
		{
			GD.PushError("Missing title in block starting at " + startPositionMediaBlock);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		return title;
	}


	private static string GetYear(string pageHtml, int startPositionMediaBlock, string title)
	{
		var indicator = "<div class=\"web-content-tile__year\">";
		var startPosition = pageHtml.IndexOf(indicator, startPositionMediaBlock) + indicator.Length;
		var endPosition = pageHtml.IndexOf('<', startPosition);
		var year = pageHtml.Mid(startPosition, endPosition - startPosition);
		if (year is null)
		{
			GD.PushError("Missing year for " + title);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		return year;
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


	private static (string?, int) GetPosterURLAndStartPosition(string pageHtml, int startPositionMediaBlock, string title, string year)
	{
		string? posterURL = null;
		var startPosition = pageHtml.IndexOf("//canvas-lb.Fandangotv.com/", startPositionMediaBlock);
		int endPosition;
		if (startPosition != -1)
		{
			endPosition = pageHtml.IndexOf('\"', startPosition);
			var posterURLSuffix = pageHtml.Mid(startPosition, endPosition - startPosition);
			posterURL = "https:" + posterURLSuffix;
		}

		if (string.IsNullOrEmpty(posterURL))
		{
			startPosition = startPositionMediaBlock;
			// GD.PushError("Missing poster URL for " + title + " - " + year);
			// (Engine.GetMainLoop() as SceneTree).Quit();
		}
		
		return (posterURL, startPosition);
	}


	private static (string?, int) GetDurationAndUpdatedStartPosition(
		ViewModel.MediaType type, 
		int startPosition, 
		string pageHtml, 
		string title, 
		string year
	)
	{
		string duration;
		if (type.Equals(ViewModel.MediaType.TVShow))
		{
			return ("? seasons", startPosition);
		}
		else
		{
			const string indicator = "<div class=\"web-content-tile__duration\">";
			startPosition = pageHtml.IndexOf(indicator, startPosition) + indicator.Length;
			var endPosition = pageHtml.IndexOf('<', startPosition);
			duration = pageHtml.Mid(startPosition, endPosition - startPosition);
			if (string.IsNullOrEmpty(duration))
			{
				GD.PushError("Missing duration for " + title + " - " + year);
				(Engine.GetMainLoop() as SceneTree).Quit();
			}
			
			return (duration, startPosition);
		}
	}


	private static (ViewModel.AgeRatings, int) GetAgeRatingAndUpdatedStartPosition(
		ViewModel.MediaType type, 
		int startPosition, 
		string pageHtml, 
		string title, 
		string year
	)
	{
		const string indicator = "<div class=\"web-rating\">";
		startPosition = pageHtml.IndexOf(indicator, startPosition) + indicator.Length;
		var endPosition = pageHtml.IndexOf('<', startPosition);
		var ageRatingAsString = pageHtml.Mid(startPosition, endPosition - startPosition);
		ViewModel.AgeRatings ageRating;
		switch (ageRatingAsString)
		{
			case "TV-Y":
				ageRating = ViewModel.AgeRatings.TVY;
				break;
			case "TV-Y7":
				ageRating = ViewModel.AgeRatings.TVY7;
				break;
			case "G":
			case "TV-G":
				ageRating = ViewModel.AgeRatings.G;
				break;
			case "PG":
			case "TV-PG":
				ageRating = ViewModel.AgeRatings.PG;
				break;
			case "PG-13":
				ageRating = ViewModel.AgeRatings.PG13;
				break;
			case "TV-14":
				ageRating = ViewModel.AgeRatings.TV14;
				break;
			case "R":
				ageRating = ViewModel.AgeRatings.R;
				break;
			case "TV-MA":
				ageRating = ViewModel.AgeRatings.TVMA;
				break;
			case "NC-17":
				ageRating = ViewModel.AgeRatings.NC17;
				break;
			case "NR":
				ageRating = ViewModel.AgeRatings.NR;
				break;
			case "":
				ageRating = ViewModel.AgeRatings.Unknown;
				break;
			default:
				GD.PushError("Unrecognized age rating for " + title + " - " + year + ": " +
				             ageRatingAsString);
				(Engine.GetMainLoop() as SceneTree).Quit();
				throw new Exception(
					"Unrecognized age rating for " + title + " - " + year + ": " + ageRatingAsString);
		}

		if (ageRating is ViewModel.AgeRatings.Unknown)
		{
			GD.PushError("Missing age rating for " + title + "-" + year);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}
		
		return (ageRating, startPosition);
	}


	private static string GetGenre(
		int startPosition, 
		string pageHtml, 
		string title, 
		string year,
		string categoryName
	)
	{
		const string indicator = "<div class=\"web-content-tile__tags\">";
		startPosition = pageHtml.IndexOf(indicator, startPosition) + indicator.Length;
		var endPosition = pageHtml.IndexOf('<', startPosition);
		var genre = pageHtml.Mid(startPosition, endPosition - startPosition)
			.Replace("&amp;", "&")
			.Replace("·&nbsp;", " ")
			.Replace("&nbsp;", " ")
			.Replace("  ", " ");
			
		if (string.IsNullOrEmpty(genre))
		{
			GD.PushError("Missing genre for " + title + " - " + year);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}
		else
		{
			genre += ", " + categoryName;
		}
		
		return genre;
	}
}