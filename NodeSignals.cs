namespace MediaNight.C_;

using System;
using Godot;


internal partial class NodeSignals : Node
{
	private static void LandingReadyResizeWindow()
	{
		Calculations.ResizeLandingWindow();
	}

	
	private static void ButtonQuitOnPressedQuit()
	{
		(Engine.GetMainLoop() as SceneTree).Root.PropagateNotification((int)Node.NotificationWMCloseRequest);
		(Engine.GetMainLoop() as SceneTree).Quit();
	}


	private static void ButtonAdvancedSettingsPressed()
	{
		
	}


	private static void ButtonStartOnPressedLoadProgressBar()
	{
		(Engine.GetMainLoop() as SceneTree).ChangeSceneToFile("res://ProgressBar.tscn");
	}


	private static void ProgressBarReadyCompileData()
	{
		Calculations.SetupProgressBars();
		Calculations.CompileDataAsync();
	}


	private static void ResultsReady()
	{
		Calculations.MatchMediaToCurrentSearchTextAndActiveFiltersAsync();

		var size = DisplayServer.ScreenGetSize();
		var root = (Engine.GetMainLoop() as SceneTree).Root;
		
		DisplayServer.WindowSetSize(size);

		FixResultsContainerSize.MarginContainer(root, size);
		FixResultsContainerSize.LineSearchEdit(root, size);
		FixResultsContainerSize.Margins(root, size);
	}


	private static void LineEditTextChangedSearch(string searchText)
	{
		Calculations.MatchMediaToCurrentSearchTextAndActiveFiltersAsync();
	}


	private static void ButtonAllOnPressedUpdateFilter()
	{
		Calculations.UpdateViewWithNewFilterChoiceAsync("MediaType.Unknown");
	}


	private static void ButtonMoviesOnPressedUpdateFilter()
	{
		Calculations.UpdateViewWithNewFilterChoiceAsync("MediaType.Movie");
	}


	private static void ButtonTVOnPressedUpdateFilter()
	{
		Calculations.UpdateViewWithNewFilterChoiceAsync("MediaType.TVShow");
	}


	private static void ButtonKidsOnPressedUpdateFilter()
	{
		Calculations.UpdateViewWithNewFilterChoiceAsync("MaxAgeRating.G");
	}
}