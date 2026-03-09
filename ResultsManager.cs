namespace MediaNight.C_;

using Godot;


public partial class ResultsManager : Node
{
	public ViewModel.MediaType Type { get; set; }
	public string Title { get; set; }
	public int Year { get; set; }

	public ViewModel.AgeRatings AgeRating { get; set; }

	// More easily searchable for partial matches than HashSet<string>
	public string Genres { get; set; }
	public string Duration { get; set; }
	public GridContainer PosterGridContainer { get; set; }
	public string MediaURL { get; set; }
	public decimal ImdbScore { get; set; }
	public string Source { get; set; }
}