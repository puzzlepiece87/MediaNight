namespace MediaNight.C_.MediaSources;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using Interfaces;
using Tools;
using Color = Godot.Color;
using HttpClient = System.Net.Http.HttpClient;


public class PBSKids : IMediaSource
{
	static PBSKids()
	{
		PBSKids.CategoryNamesByInitialSourceURL = new Dictionary<string, string>
		{
			{ "https://pbskids.org/videos/", string.Empty },
			{ "https://pbskids.org/videos/movies/", string.Empty }
		};

		PBSKids.CategoryNamesBySourceURL = new Dictionary<string, string>();
	}

	public static Dictionary<string, string> CategoryNamesByInitialSourceURL { get; set; }
	public static Dictionary<string, string> CategoryNamesBySourceURL { get; set; }
	private static string PageHtmlOfAllShowsPage { get; set; }
	private static string PageHtmlOfVideosPage { get; set; }
	private static string PageHtmlOfHomePage { get; set; }


	public static async Task SetCategoryNamesBySourceURLAsync()
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;
		
		ProgressBarManager.IncrementMaximum(PBSKids.CategoryNamesByInitialSourceURL.Count, mediaSourceName);

		using var httpClient = new HttpClient();
		PBSKids.PageHtmlOfAllShowsPage = await httpClient.GetStringAsync("https://pbskids.org/everything");
		PBSKids.PageHtmlOfVideosPage = await httpClient.GetStringAsync("https://pbskids.org/videos");
		PBSKids.PageHtmlOfHomePage = await httpClient.GetStringAsync("https://pbskids.org/");
		
		foreach (var (uRL, category) in PBSKids.CategoryNamesByInitialSourceURL)
		{
			PBSKids.CategoryNamesBySourceURL.Add(uRL, category);
			
			ProgressBarManager.IncrementValue(mediaSourceName);
		}
	}


	public static async Task<HashSet<string>> GetSearchResultsAndAddNewMediaToViewModelAsync(string sourceURL, string categoryName)
	{
		if (sourceURL.Equals("https://pbskids.org/videos/movies/"))
		{
			return await PBSKids.AdditionalMoviesWereAddedToViewModelAsync(sourceURL);
		}
		else
		{
			return await PBSKids.AdditionalShowsWereAddedToViewModelAsync(sourceURL, categoryName);
		}
	}


	private static async Task<HashSet<string>?> AdditionalShowsWereAddedToViewModelAsync(string sourceURL, string categoryName)
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;

		using var httpClient = new HttpClient();
		var pageHtml = await httpClient.GetStringAsync(sourceURL);

		const string indicator = "<li class=\"PropertiesNavigationBanner_emblaSlide__7kbWK\"";
		var startPositions = pageHtml.GetAllIndicesOfSubstring(indicator);
		ProgressBarManager.IncrementMaximum(startPositions.Count, mediaSourceName);
		
		foreach (var startPositionMediaBlock in startPositions)
		{
			var endPosition = pageHtml.IndexOf("</a>", startPositionMediaBlock);
			var mediaBlock = pageHtml.Mid(startPositionMediaBlock, endPosition - startPositionMediaBlock);
			var title = PBSKids.GetShowTitle(mediaBlock);
			const string year = "-1";
			
			if (
				ViewModel.DistinctTitleYears.Contains(title + " - " + year) || 
				ViewModel.IgnoreList.Contains(title + " - " + year)
			)
			{
				ProgressBarManager.IncrementValue(mediaSourceName);
				continue;
			}

			var mediaURL = PBSKids.GetMediaURL(pageHtml, startPositionMediaBlock, title, year);
			const ViewModel.MediaType type = ViewModel.MediaType.TVShow;
			var showLogoAndCharacterCardURLs = PBSKids.GetShowLogoAndCharacterCardURLsForPoster(title);
			const string duration = "? seasons";
			const ViewModel.AgeRatings ageRating = ViewModel.AgeRatings.TVY7;
			const string genre = "Educational";

			if (
				ageRating <= ViewModel.MaxAgeRating &&
				!ViewModel.DistinctTitleYears.Contains(title + "-" + year)
			)
			{
				ViewModel.DistinctTitleYears.Add(title + "-" + year);

				var posterGridContainer = await PBSKids.GetShowPosterGridContainerAsync(
					mediaURL, mediaSourceName, title, year, showLogoAndCharacterCardURLs,
					MediaNightTools.GetMetadataTextForPosterLabel(
						title, type, genre, duration, year, ageRating, "PBS Kids"
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
					row["Source"] = "PBS Kids";

					ViewModel.NewlyAddedMedia.Rows.Add(row);
				}
			}

			ProgressBarManager.IncrementValue(mediaSourceName);
		}
		
		// PBS Kids has no search - none of these are search results
		return null;
	}


	private static string GetShowTitle(string mediaBlock)
	{
		const string indicatorName = "<a aria-label=\"";
		var startPosition = mediaBlock.IndexOf(indicatorName) + indicatorName.Length;
		var endPosition = mediaBlock.IndexOf('\"', startPosition);
		var title = mediaBlock.Mid(startPosition, endPosition - startPosition);
		title = title.Replace("&#x27;", "'");
		title = title.Replace("&amp;", "&");
		return title;
	}


	private static async Task<string> GetMovieTitle(string mediaBlock)
	{
		const string indicatorCategoryURLSuffix = "href=\"";
		var startPosition = mediaBlock.IndexOf(indicatorCategoryURLSuffix) + indicatorCategoryURLSuffix.Length;
		var endPosition = mediaBlock.IndexOf('?', startPosition);
		var movieURL = "https://pbskids.org" + mediaBlock.Mid(startPosition, endPosition - startPosition);
		
		using var httpClient = new HttpClient();
		var pageHtml = await httpClient.GetStringAsync(movieURL);
		
		const string indicatorMovieTitle = "<title ";
		var titleBlockStartPosition = pageHtml.IndexOf(indicatorMovieTitle);
		startPosition = pageHtml.IndexOf('>', titleBlockStartPosition) + 1;
		endPosition = pageHtml.IndexOf(" Video | PBS KIDS</title>", startPosition);
		var title = pageHtml.Mid(startPosition, endPosition - startPosition);
		title = title.Replace("&#x27;", "'");
		title = title.Replace("&amp;", "&");
		return title;
	}
	
	
	public static string GetMediaURL(string pageHtml, int startPositionMediaBlock, string title, string year)
	{
		const string indicatorMediaURLSuffix = "href=\"";
		var startPosition = pageHtml.IndexOf(indicatorMediaURLSuffix, startPositionMediaBlock) + indicatorMediaURLSuffix.Length;
		var endPositionCharacter = pageHtml.Contains("https://pbskids.org/videos/movies") ? '?' : '\"';
		var endPosition = pageHtml.IndexOf(endPositionCharacter, startPosition);
		return "https://pbskids.org" + pageHtml.Mid(startPosition, endPosition - startPosition);
	}


	private static (string, string) GetShowLogoAndCharacterCardURLsForPoster(string showTitle)
	{
		var titleInHTMLFormat = PBSKids.GetShowTitleInHTMLFormat(showTitle);

		var startPositionPosterBlock = PBSKids.PageHtmlOfVideosPage.IndexOf("<a aria-label=\"" + titleInHTMLFormat);
	
		var startPositionLogoBlock = 
			PBSKids.PageHtmlOfVideosPage.IndexOf(" class=\"CharacterCard_logo_", startPositionPosterBlock);
		var startPositionCharacterCardBlock =
			PBSKids.PageHtmlOfVideosPage.IndexOf(" class=\"CharacterCard_character_", startPositionPosterBlock);

		var startPositionLogoOuterURL = 
			PBSKids.PageHtmlOfVideosPage.IndexOf("src=\"", startPositionLogoBlock);
		var startPositionCharacterCardOuterURL = 
			PBSKids.PageHtmlOfVideosPage.IndexOf("src=\"", startPositionCharacterCardBlock);

		var startPositionLogoInnerURL = 
			PBSKids.PageHtmlOfVideosPage.IndexOf("https%3A%2F%2Fcms-assets.prod.pbskids.org%2Fglobal%2F", startPositionLogoOuterURL);
		var startPositionCharacterCardInnerURL = 
			PBSKids.PageHtmlOfVideosPage.IndexOf("https%3A%2F%2Fcms-assets.prod.pbskids.org%2Fglobal%2F", startPositionCharacterCardOuterURL);

		var endPositionLogoInnerURL = 
			PBSKids.PageHtmlOfVideosPage.IndexOf(".png", startPositionLogoInnerURL) + 4;
		var endPositionCharacterCardInnerURL = 
			PBSKids.PageHtmlOfVideosPage.IndexOf(".png", startPositionCharacterCardInnerURL) + 4;

		var uRLLogo = PBSKids.PageHtmlOfVideosPage.Mid(
			startPositionLogoInnerURL, endPositionLogoInnerURL - startPositionLogoInnerURL
		);
		uRLLogo = uRLLogo.Replace("%3A", ":");
		uRLLogo = uRLLogo.Replace("%2F", "/");

		var uRLCharacterCard = PBSKids.PageHtmlOfVideosPage.Mid(
			startPositionCharacterCardInnerURL, endPositionCharacterCardInnerURL - startPositionCharacterCardInnerURL
		);
		uRLCharacterCard = uRLCharacterCard.Replace("%3A", ":");
		uRLCharacterCard = uRLCharacterCard.Replace("%2F", "/");

		return (uRLLogo, uRLCharacterCard);
	}


	private static string GetShowTitleInHTMLFormat(string showTitle)
	{
		var titleInHTMLFormat = showTitle;
		titleInHTMLFormat = titleInHTMLFormat.Replace("&", "&amp;");
		titleInHTMLFormat = titleInHTMLFormat.Replace("'", "&#x27;");
		titleInHTMLFormat = showTitle switch
		{
			// "Team Hamster! & Ruff Ruffman" => "Team Hamster &amp; Ruff",
			// "Jelly, Ben & Pogo" => "Jelly, Ben, &amp; Pogo",
			// "Let's Go Luna!" => "Let&#x27;s Go Luna",
			// "The Cat in the Hat" => "catinthehat",
			// "Mega Wow" => "PBS KIDS Mega Wow",
			_ => titleInHTMLFormat
		};
		
		return titleInHTMLFormat;
	}


	private static string GetShowTitleInUnicodeFormat(string showTitle)
	{
		var titleInUnicodeFormat = showTitle;
		titleInUnicodeFormat = titleInUnicodeFormat.Replace("&", "\\u0026");
		titleInUnicodeFormat = showTitle switch
		{
			// "Team Hamster! & Ruff Ruffman" => "Team Hamster &amp; Ruff",
			// "Jelly, Ben & Pogo" => "Jelly, Ben, &amp; Pogo",
			// "Let's Go Luna!" => "Let&#x27;s Go Luna",
			// "The Cat in the Hat" => "catinthehat",
			// "Mega Wow" => "PBS KIDS Mega Wow",
			_ => titleInUnicodeFormat
		};
		
		return titleInUnicodeFormat;
	}


	private static string GetMoviePosterURL(string mediaBlock)
	{
		const string indicatorPosterBlock = "src=\"";
		const string indicatorPosterURL = "https%3A%2F%2Fcms-assets.prod.pbskids.org%2Fglobal%2F";
		var blockStartPosition = mediaBlock.IndexOf(indicatorPosterBlock);
		var startPosition = mediaBlock.IndexOf(indicatorPosterURL, blockStartPosition);
		var endPosition = mediaBlock.IndexOf('&', startPosition);
		var moviePosterURL = mediaBlock.Mid(startPosition, endPosition - startPosition);
		moviePosterURL = moviePosterURL.Replace("%3A", ":");
		moviePosterURL = moviePosterURL.Replace("%2F", "/");
		return moviePosterURL;
	}


	private static async Task<HashSet<string>?> AdditionalMoviesWereAddedToViewModelAsync(string sourceURL)
	{
		var mediaSourceName = MethodBase.GetCurrentMethod().DeclaringType.DeclaringType.Name;
		
		using var httpClient = new HttpClient();
		var pageHtml = await httpClient.GetStringAsync(sourceURL);

		const string indicator = "<li class=\"MediaItem_mediaItem__qeyda \"";
		var startPositions = pageHtml.GetAllIndicesOfSubstring(indicator);
		ProgressBarManager.IncrementMaximum(startPositions.Count, mediaSourceName);
		
		foreach (var startPositionMediaBlock in startPositions)
		{
			var endPosition = pageHtml.IndexOf("</a>", startPositionMediaBlock);
			var mediaBlock = pageHtml.Mid(startPositionMediaBlock, endPosition - startPositionMediaBlock);
			var title = await PBSKids.GetMovieTitle(mediaBlock);
			const string year = "-1";

			if (ViewModel.DistinctTitleYears.Contains(title + " - " + year))
			{
				continue;
			}
			if (ViewModel.IgnoreList.Contains(title + " - " + year))
			{
				continue;
			}

			var mediaURL = PBSKids.GetMediaURL(pageHtml, startPositionMediaBlock, title, year);
			const ViewModel.MediaType type = ViewModel.MediaType.Movie;
			var posterURL = PBSKids.GetMoviePosterURL(mediaBlock);
			const string duration = "? minutes";
			const ViewModel.AgeRatings ageRating = ViewModel.AgeRatings.TVY7;
			var genre = string.Empty;

			if (
				ageRating <= ViewModel.MaxAgeRating &&
				!ViewModel.DistinctTitleYears.Contains(title + "-" + year)
			)
			{
				ViewModel.DistinctTitleYears.Add(title + "-" + year);

				var posterGridContainer = await Calculations.GetPosterGridContainerAsync(
					mediaURL, posterURL, mediaSourceName, title, year, 
					MediaNightTools.GetMetadataTextForPosterLabel(
						title, type, genre, duration, year, ageRating, "PBS Kids"
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
					row["Source"] = "PBS Kids";

					ViewModel.NewlyAddedMedia.Rows.Add(row);
				}
			}

			ProgressBarManager.IncrementValue(mediaSourceName);
		}

		// PBS Kids has no search - none of these are search results
		return null;
	}


	public static string? GetURLForSearchTerm(string searchTerm)
	{
		// PBS Kids doesn't have search
		return null;
	}


	private static async Task<GridContainer> GetShowPosterGridContainerAsync(
		string mediaURL,
		string mediaSourceName,
		string title,
		string year,
		(string, string) showLogoAndCharacterCardURLs,
		string metadataTextForPosterLabel
	)
	{
		var gridContainer = new GridContainer();

		var buttonPoster = await PBSKids.GetShowButtonPosterAsync(
			mediaURL, mediaSourceName, title, year, showLogoAndCharacterCardURLs
		);
		gridContainer.AddChild(buttonPoster);
		
		var gridContainerActionButtons = new GridContainer();
		gridContainerActionButtons.Columns = 2;
		gridContainerActionButtons.AddThemeConstantOverride("h_separation", 3);

		if (ViewModel.YtdlpInstalled)
		{
			var buttonDownload = new Button();
			buttonDownload.Text = "  " + char.ConvertFromUtf32(0x2193) + " - Save  ";
			buttonDownload.AddThemeFontSizeOverride("font_size", 28);
			buttonDownload.SetCustomMinimumSize(new Vector2(buttonPoster.Size.X / 2, 1));
			buttonDownload.Pressed += async () => await Calculations.DownloadAsync(mediaURL, buttonDownload);
			gridContainerActionButtons.AddChild(buttonDownload);
		}
		else
		{
			gridContainerActionButtons.AddChild(new Control());
		}
		var buttonIgnore = new Button();
		buttonIgnore.Text = " X - Ignore ";
		buttonIgnore.AddThemeFontSizeOverride("font_size", 28);
		buttonIgnore.SetCustomMinimumSize(new Vector2(buttonPoster.Size.X / 2, 1));
		buttonIgnore.GuiInput += async @event =>
			await Calculations.IgnoreTitleYearAsync(@event, buttonIgnore, gridContainer, title, year); 
		
		gridContainerActionButtons.AddChild(buttonIgnore);

		
		gridContainer.AddChild(gridContainerActionButtons);

		var labelSettings = new LabelSettings();
		labelSettings.FontSize = 28;
		
		var posterMarginContainer = Calculations.GetPosterHoverMarginContainer(metadataTextForPosterLabel, labelSettings);
		buttonPoster.AddChild(posterMarginContainer);
		buttonPoster.MouseEntered += () => Calculations.ShowDataTextMarginContainer(posterMarginContainer);
		buttonPoster.MouseExited += () => Calculations.HideDataTextMarginContainer(
			posterMarginContainer, buttonPoster.TextureNormal != buttonPoster.TextureHover
		);
		if (buttonPoster.TextureNormal != buttonPoster.TextureHover)
		{
			posterMarginContainer.SetVisible(false);
		}

		return gridContainer;
	}


	private static async Task<TextureButton> GetShowButtonPosterAsync(
		string mediaURL,
		string mediaSourceName,
		string title,
		string year,
		(string, string) showLogoAndCharacterCardURLs
	)
	{
		var buttonPoster = new TextureButton();
		buttonPoster.Pressed += () => OS.ShellOpen(mediaURL);

		var textureNormal = await PBSKids.GetShowPosterButtonTextureNormalAsync(mediaSourceName, title, year, showLogoAndCharacterCardURLs);
		if (textureNormal is not null)
		{
			buttonPoster.TextureNormal = textureNormal;

			var blackOpaqueTextureImage = Image.CreateEmpty(
				Convert.ToInt32(textureNormal.GetSize().X),
				Convert.ToInt32(textureNormal.GetSize().Y),
				false, Image.Format.La8
			);
			blackOpaqueTextureImage.Fill(new Color(0, 0, 0, 1f));
			var posterHoverTexture = ImageTexture.CreateFromImage(blackOpaqueTextureImage);
			buttonPoster.TextureHover = posterHoverTexture;
			buttonPoster.TextureFocused = posterHoverTexture;
		}
		else
		{
			var blackMostlyOpaqueTextureImage = Image.CreateEmpty(
				ViewModel.IdealHorizontalPixelsPerMediaGrid,
				ViewModel.VerticalPixelsPerMediaGrid,
				false, Image.Format.La8
			);
			blackMostlyOpaqueTextureImage.Fill(new Color(0, 0, 0, 0.8f));
			var blackOpaqueTextureImage = Image.CreateEmpty(
				ViewModel.IdealHorizontalPixelsPerMediaGrid,
				ViewModel.VerticalPixelsPerMediaGrid,
				false, Image.Format.La8
			);
			blackOpaqueTextureImage.Fill(new Color(0, 0, 0, 1f));
			var posterHoverFocusTexture = ImageTexture.CreateFromImage(blackOpaqueTextureImage);
			var posterNonHoverTexture = ImageTexture.CreateFromImage(blackMostlyOpaqueTextureImage);
			buttonPoster.TextureHover = posterHoverFocusTexture;
			buttonPoster.TextureFocused = posterHoverFocusTexture;
			buttonPoster.TextureNormal = posterNonHoverTexture;
		}
		
		return buttonPoster;
	}


	private static async Task<ImageTexture?> GetShowPosterButtonTextureNormalAsync(
		string mediaSourceName,
		string title,
		string year,
		(string, string) showLogoAndCharacterCardURLs
	)
	{
		var potentialCachedPosterFiles = new DirectoryInfo(Path.Combine(
			ViewModel.PathCache, mediaSourceName
		)).GetFiles(title.Replace("/", "\\") + "-" + year + ".*");
		
		var pathPoster = potentialCachedPosterFiles.Length == 0 ? 
			await PBSKids.GetPathShowPosterAsync(showLogoAndCharacterCardURLs, title, year) : 
			potentialCachedPosterFiles[0].FullName;

		if (pathPoster is null)
		{
			return null;
		}

		var fileAsByteArray = await File.ReadAllBytesAsync(pathPoster);
		var imagePoster = new Image();
		imagePoster = Calculations.LoadImagePosterFromBuffer(imagePoster, fileAsByteArray, pathPoster);

		if (imagePoster is null)
		{
			return null;
		}
		
		
		var originalSize = imagePoster.GetSize();

		var scale = (float)ViewModel.VerticalPixelsPerMediaGrid / originalSize.Y;
		
		imagePoster.Resize(
			Convert.ToInt32(originalSize.X * scale),
			Convert.ToInt32(originalSize.Y * scale),
			Image.Interpolation.Lanczos
		);
		
		var imageTexturePoster = new ImageTexture();
		imageTexturePoster.SetImage(imagePoster);

		return imageTexturePoster;
	}


	private static async Task<string?> GetPathShowPosterAsync(
		(string, string) showLogoAndCharacterCardURLs,
		string title,
		string year
	)
	{
		var (logoURL, characterCardURL) = showLogoAndCharacterCardURLs;

		var logoAsStream = await Calculations.GetStreamOfFileFromWebAsync(logoURL);
		var logoAsMemoryStream = new MemoryStream();
		await logoAsStream.CopyToAsync(logoAsMemoryStream);
		var logoAsByteArray = logoAsMemoryStream.ToArray();

		var characterCardAsStream = await Calculations.GetStreamOfFileFromWebAsync(characterCardURL);
		var characterCardAsMemoryStream = new MemoryStream();
		await characterCardAsStream.CopyToAsync(characterCardAsMemoryStream);
		var characterCardAsByteArray = characterCardAsMemoryStream.ToArray();

		var imageLogo = new Image();
		imageLogo = Calculations.LoadImagePosterFromBuffer(imageLogo, logoAsByteArray, "1" + Path.GetExtension(logoURL));

		if (imageLogo is null)
		{
			return null;
		}

		var imageCharacterCard = new Image();
		imageCharacterCard = Calculations.LoadImagePosterFromBuffer(
			imageCharacterCard, characterCardAsByteArray, "2" + Path.GetExtension(characterCardURL)
		);

		if (imageCharacterCard is null)
		{
			return null;
		}
		
		var imageLogoUsedRect = imageLogo.GetUsedRect();
		var imageLogoCropped = imageLogo.GetRegion(imageLogoUsedRect);

		var imageLogoCroppedSize = imageLogoCropped.GetSize();
		var imageCharacterCardSize = imageCharacterCard.GetSize();

		var titleInUnicodeFormat = PBSKids.GetShowTitleInUnicodeFormat(title);
		var backgroundColor = PBSKids.GetShowPosterBackgroundColorIfAvailable(titleInUnicodeFormat); 
		
		var scale = (float)ViewModel.VerticalPixelsPerMediaGrid / (imageLogoCroppedSize.Y + imageCharacterCardSize.Y);
		var unscaledXValue = ViewModel.IdealHorizontalPixelsPerMediaGrid / scale;
		var combinedPoster = Image.CreateEmpty(
			Convert.ToInt32(unscaledXValue),
			Convert.ToInt32(imageLogoCroppedSize.Y + imageCharacterCardSize.Y),
			false, imageLogoCropped.GetFormat()
		);
		if (backgroundColor is not null)
		{
			combinedPoster.Fill((Color)backgroundColor);
		}

		var origin = new Vector2I(0, 0);
		combinedPoster.BlendRect(
			imageLogoCropped, new Rect2I(origin, imageLogoCroppedSize), new Vector2I(
				(combinedPoster.GetSize().X - imageLogoCroppedSize.X) / 2, 0
			)
		);
		combinedPoster.BlendRect(
			imageCharacterCard, new Rect2I(origin, imageCharacterCardSize), new Vector2I(
				(combinedPoster.GetSize().X - imageCharacterCardSize.X) / 2, imageLogoCroppedSize.Y
			)
		);

		var pathPoster = Path.Combine(
			ViewModel.PathCache, "PBSKids", title.Replace("/", "\\") + "-" + year + ".png");
		combinedPoster.SavePng(pathPoster);
		return pathPoster;
	}


	private static Color? GetShowPosterBackgroundColorIfAvailable(string titleInUnicodeFormat)
	{
		var startPositionPosterBlock = PBSKids.PageHtmlOfHomePage.IndexOf(@"""title"":""" + titleInUnicodeFormat + "\"");
		if (startPositionPosterBlock == -1)
		{
			return null;
		}

		var endPositionPosterBlock = PBSKids.PageHtmlOfHomePage.IndexOf(@"""property"":", startPositionPosterBlock);
		if (endPositionPosterBlock == -1)
		{
			endPositionPosterBlock = PBSKids.PageHtmlOfHomePage.Length;
		}

		var posterBlock = PBSKids.PageHtmlOfHomePage.Mid(
			startPositionPosterBlock, endPositionPosterBlock - startPositionPosterBlock
		);

		const string colorIdentifier = @"""navigationCardColor"":""";
		if (!posterBlock.Contains(colorIdentifier))
		{
			return null;
		}
		
		var colorSystem = System.Drawing.ColorTranslator.FromHtml(
			posterBlock.Mid(posterBlock.IndexOf(colorIdentifier) + colorIdentifier.Length, 7)					
		);
		return new Color(colorSystem.R / 255f, colorSystem.G / 255f, colorSystem.B / 255f, colorSystem.A / 255f);
	}
}