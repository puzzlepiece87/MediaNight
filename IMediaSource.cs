namespace MediaNight.C_.Interfaces;

using System.Collections.Generic;
using System.Threading.Tasks;


public interface IMediaSource
{
	public static abstract Dictionary<string, string> CategoryNamesByInitialSourceURL { get; set; }
	public static abstract Dictionary<string, string> CategoryNamesBySourceURL { get; set; }
	public static abstract Task SetCategoryNamesBySourceURLAsync();
	public static abstract Task<HashSet<string>?> GetSearchResultsAndAddNewMediaToViewModelAsync(string sourceURL, string categoryName);
	public static abstract string GetMediaURL(string pageHtml, int startPositionMediaBlock, string title, string year);
	public static abstract string? GetURLForSearchTerm(string searchTerm);
}