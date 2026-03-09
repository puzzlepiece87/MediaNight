namespace MediaNight.C_;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading;
using DataSources;
using Godot;
using Interfaces;
using MediaSources;
using Environment = System.Environment;
using Tools;


public partial class ViewModel : Node
{
	public enum AgeRatings
	{
		TVY,
		TVY7,
		G,
		PG,
		PG13,
		TV14,
		R,
		TVMA,
		NC17,
		NR,
		Unknown
	}

	public enum MediaType
	{
		Movie,
		TVShow,
		LiveTV,
		Unknown
	}


	static ViewModel()
	{
		ViewModel.PathCache = Path.Join(
			Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "MediaNight cache"
		);
		ViewModel.PathIgnoreListFile = Path.Join(ViewModel.PathCache, "Ignore List.txt");
		ViewModel.IgnoreList = ViewModel.GetIgnoreList();
		
		var (exitCode, _, _) = GeneralTools.GetExitCodeOutputAndErrorOfSilentBackgroundCommand(
			"yt-dlp", new List<string> { "--help" }, null, null
		);
		ViewModel.YtdlpInstalled = (exitCode == 0);

		ViewModel.MoreSpeedFewerPosters = false;

		ViewModel.PopulateActiveMediaAndDataSources();
		ViewModel.DistinctTitleYears = new HashSet<string>();
		ViewModel.MediaResults = new OrderedDictionary<ResultsManager, decimal>();

		var dataTable = new DataTable();
		dataTable.Columns.Add("Type", typeof(MediaType));
		dataTable.Columns.Add("Title", typeof(string));
		dataTable.Columns.Add("Year", typeof(int));
		dataTable.Columns.Add("AgeRating", typeof(AgeRatings));
		dataTable.Columns.Add("Genre", typeof(string));
		dataTable.Columns.Add("Duration", typeof(string));
		dataTable.Columns.Add("PosterGridContainer", typeof(GridContainer));
		dataTable.Columns.Add("MediaURL", typeof(string));
		dataTable.Columns.Add("Source", typeof(string));

		ViewModel.NewlyAddedMedia = dataTable;
		
		ViewModel.MaxAgeRating = AgeRatings.Unknown;

		ViewModel.ImdbScoreByTitleYear = new Dictionary<string, decimal>(StringComparer.InvariantCultureIgnoreCase);
		ViewModel.GenresByTitleYear =
			new Dictionary<string, HashSet<string>>(StringComparer.InvariantCultureIgnoreCase);

		ViewModel.PortionOfResultsViewForPostersGrid = (decimal)0.80;
		// I think two rows looks great on an HD screen and use it as minimum vertical pixels for grid of a given title
		var minimumVerticalPixelsPerMediaGridAndButtons = 
			(int)(1080 * ViewModel.PortionOfResultsViewForPostersGrid / 2);
		var verticalPixelsAvailableToDisplayMediaGridsAndButtons = (int)(
			DisplayServer.ScreenGetSize().Y * ViewModel.PortionOfResultsViewForPostersGrid
		);
		ViewModel.NumberOfRows = 
			verticalPixelsAvailableToDisplayMediaGridsAndButtons / minimumVerticalPixelsPerMediaGridAndButtons;
		ViewModel.VerticalPixelsPerMediaGrid = Convert.ToInt32(
			verticalPixelsAvailableToDisplayMediaGridsAndButtons * (decimal)0.85 / ViewModel.NumberOfRows
		);
		// I think six columns looks great on an HD screen and use it as an ideal but not mandatory horizontal pixels
		// for grid of a given title
		// Small width of vertical scrollbar causes horizontal scrollbar to appear, so let's make it a tiny bit smaller 
		ViewModel.IdealHorizontalPixelsPerMediaGrid = (1920 / 6) - 1;
		
		ViewModel.MediaTypeFilter = MediaType.Unknown;
		ViewModel.MinFoundAgeRating = AgeRatings.Unknown;
		ViewModel.MaxFoundAgeRating = AgeRatings.TVY;
		ViewModel.MinFoundDuration = int.MaxValue;
		ViewModel.MaxFoundDuration = int.MinValue;
		ViewModel.MinFoundYear = int.MaxValue;
		ViewModel.MaxFoundYear = int.MinValue;
		ViewModel.LockMedia = new Lock();
		ViewModel.PathSavedMedia = Path.Join(ViewModel.PathCache, "Saved Media");
		Directory.CreateDirectory(ViewModel.PathSavedMedia);
	}

	public static ViewModel Instance { get; private set; }
	public static string PathCache { get; }
	public static string PathIgnoreListFile { get; }
	public static HashSet<string> IgnoreList { get; }
	public static bool YtdlpInstalled { get; }
	public static bool MoreSpeedFewerPosters { get; set; }
	public static HashSet<Type> ActiveMediaSources { get; set; }
	public static HashSet<Type> ActiveDataSources { get; set; }
	public static HashSet<string> DistinctTitleYears { get; set; }
	public static OrderedDictionary<ResultsManager, decimal> MediaResults { get; set; }
	public static DataTable NewlyAddedMedia { get; set; }
	public static AgeRatings MaxAgeRating { get; set; }
	public static Dictionary<string, decimal> ImdbScoreByTitleYear { get; set; }
	public static Dictionary<string, HashSet<string>> GenresByTitleYear { get; set; }
	public static decimal PortionOfResultsViewForPostersGrid { get; }
	public static int NumberOfRows { get; set; }
	public static int VerticalPixelsPerMediaGrid { get; set; }
	public static int IdealHorizontalPixelsPerMediaGrid { get; }
	public static MediaType MediaTypeFilter { get; set; }
	public static AgeRatings MinFoundAgeRating { get; set; }
	public static AgeRatings MaxFoundAgeRating { get; set; }
	public static int MinFoundYear { get; set; }
	public static int MaxFoundYear { get; set; }
	public static int MinFoundDuration { get; set; }
	public static int MaxFoundDuration { get; set; }
	public static Lock LockMedia { get; }
	public static string PathSavedMedia { get; }
	public static MarginContainer ResultsMarginContainer { get; set; }
	public static GridContainer GridContainerPosters { get; set; }


	public override void _Ready()
	{
		ViewModel.Instance = this;
	}


	private static HashSet<string> GetIgnoreList()
	{
		var ignoreList = new HashSet<string>();

		Directory.CreateDirectory(Path.GetDirectoryName(ViewModel.PathIgnoreListFile));
		if (!File.Exists(ViewModel.PathIgnoreListFile))
		{
			var ignoreListFile = File.Create(ViewModel.PathIgnoreListFile);
			ignoreListFile.Close();
			ignoreListFile.Dispose();
		}
		
		using var streamReader = new StreamReader(ViewModel.PathIgnoreListFile);
		
		string line;
		while ((line = streamReader.ReadLine()) is not null)
		{
			ignoreList.Add(line);
		}
		
		return ignoreList;
	}


	private static void PopulateActiveMediaAndDataSources()
	{
		ViewModel.ActiveMediaSources = new HashSet<Type>
		{
			typeof(Tubi),
			typeof(Pluto),
			typeof(PBSKids)
		};
		ViewModel.ActiveDataSources = new HashSet<Type>
		{
			typeof(IMDB)
		};

		foreach (var type in ViewModel.ActiveMediaSources)
		{
			if (type.IsAssignableFrom(typeof(IMediaSource)))
			{
				throw new Exception(type.Name + " must implement IMediaSource to be in ActiveMediaSources");
			}
		}

		foreach (var type in ViewModel.ActiveDataSources)
		{
			if (type.IsAssignableFrom(typeof(IDataSource)))
			{
				throw new Exception(type.Name + " must implement IDataSource to be in ActiveDataSources");
			}
		}
	}
}