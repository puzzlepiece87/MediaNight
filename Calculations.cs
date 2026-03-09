namespace MediaNight.C_;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Godot;
using PuppeteerSharp;
using Tools;
using HttpClient = System.Net.Http.HttpClient;


internal partial class Calculations : Node
{
	public static void ResizeLandingWindow()
	{
		var size = (Vector2I)((Engine.GetMainLoop() as SceneTree).Root
			.GetNode("MarginContainer") as MarginContainer).GetSize();

		DisplayServer.WindowSetSize(size);
	}

	
	public static void SetupProgressBars()
	{
		ProgressBarManager.CreateNewProgressBar("Prepare and clean cache", "Cache");

		foreach (var mediaSource in ViewModel.ActiveMediaSources)
		{
			ProgressBarManager.CreateNewProgressBar(
				"Retrieving data from " + mediaSource.Name, mediaSource.Name
			);
		}

		foreach (var dataSource in ViewModel.ActiveDataSources)
		{
			ProgressBarManager.CreateNewProgressBar(
				"Retrieving data from " + dataSource.Name, dataSource.Name
			);
		}

		ProgressBarManager.CreateNewProgressBar("Prepare and display results");
		
		var size = DisplayServer.ScreenGetSize();
		DisplayServer.WindowSetSize(new Vector2I(
			Convert.ToInt32(size.X * 0.60), 
			Convert.ToInt32(size.Y * 0.12 * ProgressBarManager.Count)
		));
	}

	
	public static async Task CompileDataAsync()
	{
		Calculations.PrepareAndCleanCache();
		await new BrowserFetcher().DownloadAsync();
		await Calculations.RetrieveMediaAndDataAsync();
		Calculations.MatchIMDBScoresToMedia();
		await Task.Run(Calculations.PairIMDBScoresWithNewTitlesAndAddToMedia);

		(Engine.GetMainLoop() as SceneTree).ChangeSceneToFile("res://Results.tscn");
	}


	private static void PrepareAndCleanCache()
	{
		ProgressBarManager.IncrementMaximum(1, "Cache");
		ProgressBarManager.SetDisplayedTaskText("Preparing and cleaning cache", "Cache");

		foreach (var activeMediaSource in ViewModel.ActiveMediaSources)
		{
			var cachePath = Path.Join(ViewModel.PathCache, activeMediaSource.Name + Path.DirectorySeparatorChar);
			Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

			foreach (var file in new DirectoryInfo(cachePath).GetFiles())
			{
				if (file.CreationTime < DateTime.Now.AddDays(-60))
				{
					file.Delete();
				}
			}
		}

		foreach (var activeDataSource in ViewModel.ActiveDataSources)
		{
			var cachePath = Path.Join(ViewModel.PathCache, activeDataSource.Name + Path.DirectorySeparatorChar);
			Directory.CreateDirectory(Path.GetDirectoryName(cachePath));

			foreach (var file in new DirectoryInfo(cachePath).GetFiles())
			{
				if (file.CreationTime < DateTime.Now.AddDays(-7))
				{
					file.Delete();
				}
			}
		}

		ProgressBarManager.IncrementValue("Cache");
	}


	private static async Task RetrieveMediaAndDataAsync()
	{
		var tasks = new List<Task>
		{
			Calculations.SaveSourceMediaToViewModelAsync(),
			Calculations.RefreshIMDBDataAsync()
		};

		await Task.WhenAll(tasks);
	}


	private static async Task SaveSourceMediaToViewModelAsync()
	{
		await Parallel.ForEachAsync(
			ViewModel.ActiveMediaSources, async (mediaSource, cancellationToken) =>
			{
				await Calculations.SaveMediaSourceToViewModelAsync(mediaSource);
			}
		);
	}


	private static async Task SaveMediaSourceToViewModelAsync(Type mediaSource)
	{
		try
		{
			await (Task)mediaSource.GetMethod("SetCategoryNamesBySourceURLAsync").Invoke(null, null);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.Message);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}
		var categoryNameBySourceURL = mediaSource.GetProperty("CategoryNamesBySourceURL")
			.GetValue(null) as System.Collections.Generic.Dictionary<string, string>;
		ProgressBarManager.IncrementMaximum(categoryNameBySourceURL.Count, mediaSource.Name);

		var mediaSourceName = mediaSource.Name;

		foreach (var (sourceURL, categoryName) in categoryNameBySourceURL)
		{
			var categoryNameLessBlank = categoryName;
			var startPosition = sourceURL.LastIndexOf('/') + 1;
			if (string.IsNullOrEmpty(categoryName) && startPosition < sourceURL.Length)
			{
				categoryNameLessBlank = StringExtensions.Right(sourceURL, sourceURL.Length - startPosition);
			}
			
			ProgressBarManager.SetDisplayedTaskText(
				"Retrieving " + categoryNameLessBlank + " data from " + mediaSourceName, mediaSource.Name
			);
			await (Task)mediaSource.GetMethod("GetSearchResultsAndAddNewMediaToViewModelAsync")
				.Invoke(null, new object[] { sourceURL, categoryName });
			ProgressBarManager.IncrementValue(mediaSource.Name);
		}
	}


	public static async Task<GridContainer> GetPosterGridContainerAsync(
		string mediaURL,
		string posterURL,
		string mediaSourceName,
		string title,
		string year,
		string metadataTextForPosterLabel
	)
	{
		var gridContainer = new GridContainer();

		var (buttonPoster, posterFound) = await Calculations.GetButtonPosterAndPosterFoundAsync(
			mediaURL, posterURL, mediaSourceName, title, year
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
			// buttonDownload.SetCustomMinimumSize(new Vector2(buttonPoster.GetSize().X / 2, 1));
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
		// buttonIgnore.SetCustomMinimumSize(new Vector2(buttonPoster.GetSize().X / 2, 1));
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
			posterMarginContainer, posterFound
		);
		if (posterFound)
		{
			posterMarginContainer.SetVisible(false);
		}

		return gridContainer;
	}


	private static async Task<(TextureButton, bool)> GetButtonPosterAndPosterFoundAsync(
		string mediaURL,
		string posterURL,
		string mediaSourceName,
		string title,
		string year
	)
	{
		var buttonPoster = new TextureButton();
		buttonPoster.Pressed += () => OS.ShellOpen(mediaURL);

		var textureNormal = await Calculations.GetPosterButtonTextureNormalAsync(posterURL, mediaSourceName, title, year);
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
			
			var styleBoxFlat = new StyleBoxFlat();
			styleBoxFlat.ContentMarginLeft = 4;
			styleBoxFlat.ContentMarginTop = 4;
			styleBoxFlat.ContentMarginRight = 4;
			styleBoxFlat.ContentMarginBottom = 4;
			styleBoxFlat.BorderWidthLeft = 4;
			styleBoxFlat.BorderWidthTop = 4;
			styleBoxFlat.BorderWidthRight = 4;
			styleBoxFlat.BorderWidthBottom = 4;
			styleBoxFlat.SetBorderColor(Colors.White);
			buttonPoster.CallDeferred("add_theme_stylebox_override", "hover", styleBoxFlat);
			// GD.Print("Attempted border creation for " + title + " - " + year);
		}
		
		return (buttonPoster, textureNormal is not null);
	}


	private static async Task<ImageTexture?> GetPosterButtonTextureNormalAsync(
		string posterURL,
		string mediaSourceName,
		string title,
		string year
	)
	{
		var extension = Calculations.GetPosterFileExtension(posterURL, mediaSourceName, title, year);

		if (extension is null)
		{
			return null;
		}
		
		var pathPotentialCachedPoster = Path.Join(
			ViewModel.PathCache, mediaSourceName, title.Replace("/", "\\") + "-" + year + extension
		);

		if (!File.Exists(pathPotentialCachedPoster))
		{
			var streamPoster = await Calculations.GetStreamOfFileFromWebAsync(posterURL);
			await using var fileStream = File.Create(pathPotentialCachedPoster);
			await streamPoster.CopyToAsync(fileStream);
		}

		var fileAsByteArray = await File.ReadAllBytesAsync(pathPotentialCachedPoster);
		var imagePoster = new Image();
		imagePoster = Calculations.LoadImagePosterFromBuffer(imagePoster, fileAsByteArray, pathPotentialCachedPoster);

		if (imagePoster is null)
		{
			return null;
		}
		
		
		var originalSize = imagePoster.GetSize();

		var scale = Math.Min(
			(float)ViewModel.VerticalPixelsPerMediaGrid / originalSize.Y, 
			(float)ViewModel.IdealHorizontalPixelsPerMediaGrid / originalSize.X
		);
		
		imagePoster.Resize(
			Convert.ToInt32(originalSize.X * scale),
			Convert.ToInt32(originalSize.Y * scale),
			Image.Interpolation.Lanczos
		);
		
		var imageTexturePoster = new ImageTexture();
		imageTexturePoster.SetImage(imagePoster);
		return imageTexturePoster;
	}


	public static string? GetPosterFileExtension(
		string posterURL,
		string mediaSourceName,
		string title,
		string year
	)
	{
		if (!string.IsNullOrEmpty(posterURL))
		{
			var extension = posterURL.Substring(posterURL.LastIndexOf('.'));
			if (posterURL.Contains("//canvas-lb.tubitv.com/"))
			{
				extension = ".webp";
			}
			
			return extension;
		}
		else
		{
			var potentialCachedPosterFiles = new DirectoryInfo(Path.Combine(
				ViewModel.PathCache, mediaSourceName
			)).GetFiles(title.Replace("/", "\\") + "-" + year + ".*");

			return potentialCachedPosterFiles.Length == 0 ? null : potentialCachedPosterFiles[0].Extension;
		}
	}


	public static async Task<Stream> GetStreamOfFileFromWebAsync(string pathWeb)
	{
		bool tryAgain;
		var streamFile = Stream.Null;
		do
		{
			tryAgain = false;
			using var httpClient = new HttpClient();
			try
			{
				streamFile = await httpClient.GetStreamAsync(pathWeb);
			}
			catch (HttpRequestException exception)
			{
				switch (exception.Message)
				{
					case
						"A connection attempt failed because the connected party did not properly respond after a period of time, or established connection failed because connected host has failed to respond. (shared.akamai.steamstatic.com:443)"
						:
						// Too many requests, slow down
						await Task.Delay(1000);
						tryAgain = true;
						break;
					case "Response status code does not indicate success: 502 (Bad Gateway).":
					case "The SSL connection could not be established, see inner exception.":
						tryAgain = true;
						break;
					default:
						throw;
				}
			}
		} while (tryAgain);

		return streamFile;
	}


	public static Image? LoadImagePosterFromBuffer(Image imagePoster, byte[] fileAsByteArray, string pathPotentialCachedPoster)
	{
		var extension = Path.GetExtension(pathPotentialCachedPoster);

		Error error = 0; 
		switch (extension)
		{
			case ".webp":
				if (pathPotentialCachedPoster.Contains("/Tubi/"))
				{
					// Tubi poster file extensions are .webp but file encoding is jpg
					error = imagePoster.LoadJpgFromBuffer(fileAsByteArray);
				}
				else
				{
					error = imagePoster.LoadWebpFromBuffer(fileAsByteArray);
				}

				break;
			case ".jpg":
			case ".jpeg":
				error = imagePoster.LoadJpgFromBuffer(fileAsByteArray);
				break;
			case ".png":
				error = imagePoster.LoadPngFromBuffer(fileAsByteArray);
				break;
			default:
				GD.PushError("Unrecognized poster file format: " + pathPotentialCachedPoster);
				break;
		}

		if (error == 0)
		{
			return imagePoster;
		}
		else
		{
			GD.PushError("Error loading poster file " + pathPotentialCachedPoster);
			return null;
		}
	}


	private static async Task RefreshIMDBDataAsync()
	{
		ProgressBarManager.SetDisplayedTaskText("Retrieving IMDB data", "IMDB");

		var tasks = new List<Task>
		{
			Calculations.RefreshOneKindOfIMDBDataAsync("title.basics.tsv"),
			Calculations.RefreshOneKindOfIMDBDataAsync("title.ratings.tsv")
		};

		await Task.WhenAll(tasks);
	}


	private static void MatchIMDBScoresToMedia()
	{
		ProgressBarManager.IncrementMaximum(1, "IMDB");
		ProgressBarManager.SetDisplayedTaskText("Matching IMDB data to media", "IMDB");

		var basicsDictionary = Calculations.ProcessBasicsTSV();
		Calculations.ProcessRatingsTSV(basicsDictionary);

		ProgressBarManager.IncrementValue("IMDB");
	}


	private static async Task RefreshOneKindOfIMDBDataAsync(string tsvName)
	{
		var pathTSV = Path.Join(ViewModel.PathCache, "IMDB", tsvName);

		var fileTSV = new FileInfo(pathTSV);

		ProgressBarManager.IncrementMaximum(1, "IMDB");
		if (fileTSV.CreationTime < DateTime.Today)
		{
			var streamTSV =
				await Calculations.GetStreamOfFileFromWebAsync("https://datasets.imdbws.com/" + tsvName + ".gz");
			var pathTSVCompressed = pathTSV + ".gz";
			await using var fileStream = File.Create(pathTSVCompressed);
			await streamTSV.CopyToAsync(fileStream);
			fileStream.Close();
			var fileTSVCompressed = new FileInfo(pathTSVCompressed);
			await using var compressedFileStream = fileTSVCompressed.OpenRead();
			await using var decompressedFileStream = File.Create(pathTSV);
			await using var decompressionStream = new GZipStream(compressedFileStream, CompressionMode.Decompress);
			await decompressionStream.CopyToAsync(decompressedFileStream);
		}

		ProgressBarManager.IncrementValue("IMDB");
	}


	private static System.Collections.Generic.Dictionary<string, string> ProcessBasicsTSV()
	{
		var pathTSV = Path.Join(ViewModel.PathCache, "IMDB", "title.basics.tsv");

		var titleYearsByTitleId = new System.Collections.Generic.Dictionary<string, string>();

		using var streamReader = new StreamReader(pathTSV);
		string line;
		// Skip header
		streamReader.ReadLine();
		while ((line = streamReader.ReadLine()) is not null)
		{
			var lineAsArray = line.Split('\t');
			var titleId = lineAsArray[0];

			var type = lineAsArray[1];

			if (type.Equals("tvEpisode"))
			{
				continue;
			}

			var title = lineAsArray[2];
			var year = lineAsArray[5];
			var genresOnThisRow = lineAsArray[8].Split(',').ToHashSet();

			string basicsTitleYear;
			if (type.Equals("tvSeries") || type.Equals("tvMiniSeries"))
			{
				basicsTitleYear = title + " - tvSeries";
			}
			else
			{
				basicsTitleYear = title + " - " + year;
			}

			titleYearsByTitleId.TryAdd(titleId, basicsTitleYear);

			if (ViewModel.GenresByTitleYear.TryGetValue(basicsTitleYear, out var savedGenres))
			{
				foreach (var genre in genresOnThisRow)
				{
					savedGenres.Add(genre);
				}
			}
			else
			{
				ViewModel.GenresByTitleYear.Add(basicsTitleYear, genresOnThisRow);
			}
		}

		return titleYearsByTitleId;
	}


	private static void ProcessRatingsTSV(System.Collections.Generic.Dictionary<string, string> titleYearsByTitleId)
	{
		var pathTSV = Path.Join(ViewModel.PathCache, "IMDB", "title.ratings.tsv");

		using var streamReader = new StreamReader(pathTSV);
		string line;
		// Skip header
		streamReader.ReadLine();
		while ((line = streamReader.ReadLine()) is not null)
		{
			var lineAsArray = line.Split('\t');
			if (!titleYearsByTitleId.TryGetValue(lineAsArray[0], out var titleYear))
			{
				continue;
			}

			var rating = Convert.ToDecimal(lineAsArray[1]);
			var numberOfRatings = Convert.ToInt32(lineAsArray[2]);
			if (numberOfRatings < 100)
			{
				rating = Math.Min(rating, (decimal)8.0);
			}

			if (numberOfRatings < 500)
			{
				rating = Math.Min(rating, (decimal)9.0);
			}

			ViewModel.ImdbScoreByTitleYear.TryAdd(titleYear, rating);
		}
	}


	private static void PairIMDBScoresWithNewTitlesAndAddToMedia()
	{
		var progressBarSceneActive = ViewModel.MediaResults.Count == 0;
		
		if (progressBarSceneActive)
		{
			ProgressBarManager.IncrementMaximum(ViewModel.NewlyAddedMedia.Rows.Count);
			ProgressBarManager.SetDisplayedTaskText("Creating sorted display of media posters and data");
		}

		foreach (DataRow row in ViewModel.NewlyAddedMedia.Rows)
		{
			var type = (ViewModel.MediaType)row["Type"];
			var title = (string)row["Title"];
			var year = (int)row["Year"];
			var ageRating = (ViewModel.AgeRatings)row["AgeRating"];
			var genres = (string)row["Genre"];
			if (ViewModel.GenresByTitleYear.TryGetValue(title + " - " + year, out var imdbGenres))
			{
				imdbGenres.Add(genres);
				genres = string.Join(", ", imdbGenres);
			}

			var duration = (string)row["Duration"];
			var posterGridContainer = (GridContainer)row["PosterGridContainer"];
			var mediaURL = (string)row["MediaURL"];
			var source = (string)row["Source"];

			var resultsManager = new ResultsManager
			{
				Type = type,
				Title = title,
				Year = year,
				AgeRating = ageRating,
				Genres = genres,
				Duration = duration,
				PosterGridContainer = posterGridContainer,
				MediaURL = mediaURL,
				Source = source
			};
			// Tubi shows TV series end year, IMDB shows begin year 
			if (type == ViewModel.MediaType.TVShow)
			{
				if (ViewModel.ImdbScoreByTitleYear.TryGetValue(title + " - tvSeries", out var score))
				{
					resultsManager.ImdbScore = score;
				}
				// GD.Print("TV Series: No match found for " + title + " - " + year);
			}
			else
			{
				if (ViewModel.ImdbScoreByTitleYear.TryGetValue(title + " - " + year, out var score))
				{
					resultsManager.ImdbScore = score;
				}
				// GD.Print("Movie: No match found for " + title + " - " + year);
			}

			ViewModel.MediaResults.Add(resultsManager, resultsManager.ImdbScore);
			Calculations.UpdateViewModelFilterRanges(resultsManager);
			if (progressBarSceneActive)
			{
				ProgressBarManager.IncrementValue();
			}
		}
		
		ViewModel.NewlyAddedMedia.Clear();
	}


	private static void UpdateViewModelFilterRanges(ResultsManager resultsManager)
	{
		ViewModel.MinFoundAgeRating =
			ViewModel.MinFoundAgeRating > resultsManager.AgeRating
				? resultsManager.AgeRating
				: ViewModel.MinFoundAgeRating;
		ViewModel.MaxFoundAgeRating =
			ViewModel.MaxFoundAgeRating > resultsManager.AgeRating
				? ViewModel.MaxFoundAgeRating
				: resultsManager.AgeRating;
		// ViewModel.MinFoundDuration = Math.Min(ViewModel.MinFoundDuration, resultsManager.Duration);
		// ViewModel.MaxFoundDuration = Math.Max(ViewModel.MaxFoundDuration, resultsManager.Duration);
		ViewModel.MinFoundYear = Math.Min(ViewModel.MinFoundYear, resultsManager.Year);
		ViewModel.MaxFoundYear = Math.Max(ViewModel.MaxFoundYear, resultsManager.Year);
	}


	private static async Task DisplayResultsAsync(string searchTextNow, HashSet<string>? searchResults)
	{
		var orderedMediaResults = await Task.Run(
			() => Calculations.GetOrderedMediaResultsAndSetVisibility(searchTextNow, searchResults)
		);
		await Task.Run(() => Calculations.ReorderMediaResultsWithinGrid(orderedMediaResults));
	}


	private static List<ResultsManager> GetOrderedMediaResultsAndSetVisibility(
		string searchTextNow,
		HashSet<string>? searchResults
	)
	{
		var orderedMediaResults = new List<ResultsManager>();
		var orderedSourceMatches = new List<ResultsManager>();
		var orderedTitleMatches = new List<ResultsManager>();
		var orderedSearchMatches = new List<ResultsManager>();
		
		var orderedByRatingThenTitle = ViewModel.MediaResults.OrderByDescending(kvp => kvp.Value)
			.ThenBy(kvp => kvp.Key.Title);
		
		var searchTextAsWords = searchTextNow.Split(" ");

		// Return search results in this order:
		// 1. Search term matches source name
		// 2. Title contains search term
		// 3. Service returns media as search result
		foreach (var (media, _) in orderedByRatingThenTitle)
		{
			var genres = media.Genres;
			var genresContainsSearchWords = true;
			foreach (var word in searchTextAsWords)
			{
				if (!genres.ContainsCaseInsensitive(word))
				{
					genresContainsSearchWords = false;
				}
			}

			var source = media.Source;
			var title = media.Title;
			
			media.PosterGridContainer.CallDeferred("set_visible", (
				source.ContainsCaseInsensitive(searchTextNow)
				|| title.ContainsCaseInsensitive(searchTextNow)
				|| genresContainsSearchWords
				|| (
					searchResults is not null
					&& searchResults.Contains(string.Join("|", title, media.Year, source))
				)
			)
			&& media.AgeRating <= ViewModel.MaxAgeRating
			&& (
				ViewModel.MediaTypeFilter.Equals(ViewModel.MediaType.Unknown)
				|| media.Type.Equals(ViewModel.MediaTypeFilter)
			));

			if (source.ContainsCaseInsensitive(searchTextNow))
			{
				orderedSourceMatches.Add(media);
			}
			if (!source.ContainsCaseInsensitive(searchTextNow) && title.ContainsCaseInsensitive(searchTextNow))
			{
				orderedTitleMatches.Add(media);
			}
			if (!source.ContainsCaseInsensitive(searchTextNow) && !title.ContainsCaseInsensitive(searchTextNow))
			{
				orderedSearchMatches.Add(media);
			}
		}

		orderedMediaResults.AddRange(orderedSourceMatches);
		orderedMediaResults.AddRange(orderedTitleMatches);
		orderedMediaResults.AddRange(orderedSearchMatches);
		
		return orderedMediaResults;
	}
	
	
	private static void ReorderMediaResultsWithinGrid(List<ResultsManager> orderedMediaResults)
	{
		var index = -1;
		foreach (var mediaResult in orderedMediaResults)
		{
			index++;
			var posterGridContainer = mediaResult.PosterGridContainer;
			if (posterGridContainer.GetParent() is null)
			{
				ViewModel.GridContainerPosters.CallDeferred("add_child", posterGridContainer);
			}
			ViewModel.GridContainerPosters.CallDeferred("move_child", posterGridContainer, index);
		}
	}


	public static MarginContainer GetPosterHoverMarginContainer(string dataText, LabelSettings labelSettings)
	{
		var marginContainer = new MarginContainer();
		marginContainer.AddThemeConstantOverride("margin_left", 20);
		marginContainer.AddThemeConstantOverride("margin_right", 20);
		marginContainer.AddThemeConstantOverride("margin_top", 20);
		marginContainer.AddThemeConstantOverride("margin_bottom", 20);

		var label = new Label();
		label.Text = dataText;
		label.LabelSettings = labelSettings;

		marginContainer.AddChild(label);
		return marginContainer;
	}


	public static void ShowDataTextMarginContainer(MarginContainer marginContainer)
	{
		marginContainer.SetVisible(true);
	}


	public static void HideDataTextMarginContainer(MarginContainer marginContainer, bool mediaHasPoster)
	{
		if (mediaHasPoster)
		{
			marginContainer.SetVisible(false);
		}
	}


	public static async Task UpdateViewWithNewFilterChoiceAsync(string filterToApply)
	{
		switch (filterToApply)
		{
			case "MediaType.Unknown":
				ViewModel.MediaTypeFilter = ViewModel.MediaType.Unknown;
				ViewModel.MaxAgeRating = ViewModel.AgeRatings.Unknown;
				break;
			case "MediaType.Movie":
				ViewModel.MediaTypeFilter = ViewModel.MediaType.Movie;
				break;
			case "MediaType.TVShow":
				ViewModel.MediaTypeFilter = ViewModel.MediaType.TVShow;
				break;
			case "MaxAgeRating.G":
				ViewModel.MaxAgeRating = ViewModel.AgeRatings.G;
				break;
			default:
				throw new Exception("Unrecognized filter choice: " + filterToApply);
		}

		await Calculations.MatchMediaToCurrentSearchTextAndActiveFiltersAsync();
	}


	public static async Task MatchMediaToCurrentSearchTextAndActiveFiltersAsync()
	{
		var root = (Engine.GetMainLoop() as SceneTree).Root;
		var lineEditSearch = root.GetNode(
			"MarginContainer/GridContainer/GridContainerMenu/LineEditSearch"
		) as LineEdit;
		var searchTextThen = lineEditSearch.Text;
		await Task.Delay(800);
		var searchTextNow = lineEditSearch.Text;

		if (!searchTextThen.Equals(searchTextNow))
		{
			return;
		}
		
		if (string.IsNullOrEmpty(searchTextNow))
		{
			await Calculations.MatchMediaToActiveFiltersAsync();
			return;
		}

		HashSet<string>? searchResults = null; 
		foreach (var typeActiveMediaSource in ViewModel.ActiveMediaSources)
		{
			var sourceURL = typeActiveMediaSource.GetMethod("GetURLForSearchTerm")
				.Invoke(null, new object[] { searchTextNow }) as string;
			if (!string.IsNullOrEmpty(sourceURL))
			{
				GD.Print(typeActiveMediaSource.Name + " search happening");
				searchResults = await (Task<HashSet<string>?>)typeActiveMediaSource
					.GetMethod("GetSearchResultsAndAddNewMediaToViewModelAsync")
					.Invoke(null, new object[] { sourceURL, string.Empty });
				GD.Print(typeActiveMediaSource.Name + " search done");
			}
		}

		if (ViewModel.NewlyAddedMedia.Rows.Count > 0)
		{
			await Task.Run(Calculations.PairIMDBScoresWithNewTitlesAndAddToMedia);
		}
		
		await Calculations.DisplayResultsAsync(searchTextNow, searchResults);
	}


	private static async Task MatchMediaToActiveFiltersAsync()
	{
		var orderedByRatingThenTitle = ViewModel.MediaResults.OrderByDescending(kvp => kvp.Value)
			.ThenBy(kvp => kvp.Key.Title);

		var orderedMediaResults = new List<ResultsManager>();

		foreach (var (media, _) in orderedByRatingThenTitle)
		{
			orderedMediaResults.Add(media);
		}

		var reorderAndVisibilityTasks = new List<Task>();
		reorderAndVisibilityTasks.Add(
			Task.Run(() => Calculations.ReorderMediaResultsWithinGrid(orderedMediaResults))
		);
		reorderAndVisibilityTasks.Add(
			Task.Run(() =>
			{
				foreach (var media in ViewModel.MediaResults.Keys)
				{
					media.PosterGridContainer.CallDeferred("set_visible", 
						media.AgeRating <= ViewModel.MaxAgeRating
						&& (
							ViewModel.MediaTypeFilter.Equals(ViewModel.MediaType.Unknown)
							|| media.Type.Equals(ViewModel.MediaTypeFilter)
						)
					);
				}
			})
		);
		
		await Task.WhenAll(reorderAndVisibilityTasks);
	}


	public static async Task DownloadAsync(string mediaURL, Button buttonDownloadPressed)
	{
		try
		{
			await Task.Run(() =>
				{
					var (exitCode, _, error) = GeneralTools.GetExitCodeOutputAndErrorOfSilentBackgroundCommand(
						"yt-dlp", new List<string> { mediaURL },
						(string x) => buttonDownloadPressed.Text = ExtensionMethods.Left(x, 50), null
					);

					if (exitCode == 0)
					{
						var styleBoxFlat = new StyleBoxFlat();
						styleBoxFlat.BgColor = Colors.SeaGreen;
						buttonDownloadPressed.CallDeferred("add_theme_stylebox_override", "normal", styleBoxFlat);
						buttonDownloadPressed.CallDeferred("set_text", " " + char.ConvertFromUtf32(0x2713) + " - Success ");
					}

					if (exitCode == 1 && error.Contains("This video is DRM protected"))
					{
						var styleBoxFlat = new StyleBoxFlat();
						styleBoxFlat.BgColor = Colors.DarkRed;
						buttonDownloadPressed.CallDeferred("add_theme_stylebox_override", "normal", styleBoxFlat);
						buttonDownloadPressed.CallDeferred("set_text", "  DRMed  ");
					}
				}
			);
		}
		catch (Exception exception)
		{
			GD.PushError(exception.Message);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}
	}


	public static async Task IgnoreTitleYearAsync(
		InputEvent eventPotentialMouseClick,
		Button buttonIgnore,
		GridContainer gridContainer, 
		string title, 
		string year
	)
	{
		if (
			eventPotentialMouseClick is not InputEventMouseButton || 
			!eventPotentialMouseClick.IsPressed()
		)
		{
			return;
		}
		
		var eventAsInputEventMouseButton = eventPotentialMouseClick as InputEventMouseButton;

		if (eventAsInputEventMouseButton.ButtonIndex != MouseButton.Left)
		{
			return;
		}

		const string confirmText = "Confirm?";
		if (!buttonIgnore.Text.Equals(confirmText))
		{
			var originalText = buttonIgnore.Text;
			buttonIgnore.Text = confirmText;
			await Task.Delay(4000);
			if (gridContainer.Visible)
			{
				buttonIgnore.Text = originalText;
			}
		}
		else
		{
			gridContainer.Visible = false;
			ViewModel.IgnoreList.Add(title + " - " + year);
			
			var mediaToIgnore = new List<ResultsManager>();
			foreach (var media in ViewModel.MediaResults.Keys)
			{
				if (media.Title.Equals(title) && media.Year.ToString().Equals(year))
				{
					mediaToIgnore.Add(media);
				}
			}
			foreach (var media in mediaToIgnore)
			{
				ViewModel.MediaResults.Remove(media);
			}
			
			await using var streamWriter = new StreamWriter(ViewModel.PathIgnoreListFile, append: true);
			await streamWriter.WriteLineAsync(title + " - " + year);
		}
	}
}