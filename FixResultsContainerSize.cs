namespace MediaNight;

using System;
using C_;
using Godot;


internal partial class FixResultsContainerSize : Node
{
	public static void MarginContainer(Window root, Vector2I size)
	{
		ViewModel.ResultsMarginContainer = root.GetNode("MarginContainer") as MarginContainer;
		ViewModel.ResultsMarginContainer.CallDeferred("set_size", size);
	}
	
	
	public static void LineSearchEdit(Window root, Vector2I size)
	{
		var gridContainerMenu = root.GetNode("MarginContainer/GridContainer/GridContainerMenu") as GridContainer;
		var gridContainerMenuSize = gridContainerMenu.Size;
		var charactersToExpandTheSearchBoxBy = (int)(0.9 * size.X - gridContainerMenuSize.X) / 40;

		if (charactersToExpandTheSearchBoxBy > 0)
		{
			var lineEditSearch = root.GetNode(
				"MarginContainer/GridContainer/GridContainerMenu/LineEditSearch"
			) as LineEdit;
			var minimumCharacterWidth = lineEditSearch.GetThemeConstant("minimum_character_width");
			lineEditSearch.AddThemeConstantOverride(
				"minimum_character_width", minimumCharacterWidth + charactersToExpandTheSearchBoxBy
			);
		}
	}
	
	
	private static void ScrollContainer()
	{
		var size = DisplayServer.ScreenGetSize();
		var root = (Engine.GetMainLoop() as SceneTree).Root;
		
		GD.Print("Fixing ScrollContainer size");
		var scrollContainer = root.GetNode("MarginContainer/GridContainer/ScrollContainer") as ScrollContainer;
		scrollContainer.Size = new Vector2(size.X, Convert.ToInt32(size.Y * ViewModel.PortionOfResultsViewForPostersGrid));
		scrollContainer.VerticalScrollMode = Godot.ScrollContainer.ScrollMode.ShowAlways;

		FixResultsContainerSize.GridContainerPosters();
	}
	
	
	private static void GridContainerPosters()
	{
		var size = DisplayServer.ScreenGetSize();

		GD.Print("Fixing GridContainerPosters size");
		ViewModel.GridContainerPosters.Size = 
			new Vector2(size.X, Convert.ToInt32(size.Y * ViewModel.PortionOfResultsViewForPostersGrid));
	}


	public static void Margins(Window root, Vector2I size)
	{
		ViewModel.GridContainerPosters = root.GetNode(
			"MarginContainer/GridContainer/ScrollContainer/GridContainerPosters"
		) as GridContainer;

		if (ViewModel.NumberOfRows == 1)
		{
			ViewModel.GridContainerPosters.Columns = ViewModel.MediaResults.Count;
		}
		else
		{
			ViewModel.GridContainerPosters.Columns = Convert.ToInt32(size.X / ViewModel.IdealHorizontalPixelsPerMediaGrid);
		}

		var remainder = size.X - (ViewModel.GridContainerPosters.Columns * ViewModel.IdealHorizontalPixelsPerMediaGrid);
		if (remainder > 0)
		{
			var evenlyDividedRemainder = remainder / (ViewModel.GridContainerPosters.Columns + 1);
			var marginLeftRight = Math.Min(evenlyDividedRemainder, 20);
			var hSeparationValue = (remainder - (2 * marginLeftRight)) / (ViewModel.GridContainerPosters.Columns - 1);
			ViewModel.ResultsMarginContainer.CallDeferred(
				"add_theme_constant_override", "margin_left", marginLeftRight
			);
			ViewModel.ResultsMarginContainer.CallDeferred(
				"add_theme_constant_override", "margin_right", marginLeftRight
			);
			ViewModel.GridContainerPosters.CallDeferred(
				"add_theme_constant_override", "h_separation", hSeparationValue
			);
		}
	}
}