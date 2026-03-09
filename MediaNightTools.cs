namespace MediaNight.C_.Tools;

using System;
using System.IO;
using System.Threading.Tasks;
using Godot;
using PuppeteerSharp;
using Environment = System.Environment;


public static class MediaNightTools
{
	public static string GetMetadataTextForPosterLabel(
		string title,
		ViewModel.MediaType type,
		string genre,
		string duration,
		string year,
		ViewModel.AgeRatings ageRating,
		string source
	)
	{
		return string.Join((string?)Environment.NewLine,
			"Title: " + title,
			"Type: " + type,
			"Genre: " + genre,
			"Duration: " + duration,
			"Year: " + year,
			"Rated: " + ageRating,
			"Source: " + source
		);
	}


	public static async Task<string?> GetFinalHTMLViaPuppeteerAsync(
		IBrowser browser,
		string selector,
		string mediaSourceName,
		string thingBeingRetrieved,
		string uRLTarget
	)
	{
		await using var page = await browser.NewPageAsync();
		await page.SetUserAgentAsync("Mozilla/5.0 (X11; Linux x86_64; rv:147.0) Gecko/20100101 Firefox/147.0");
		const int maxAttempts = 3;
		
		var pageHtml = string.Empty;
		var attemptNumber = 0;
		while (!pageHtml.Contains(selector) && !pageHtml.Contains(selector.Right(selector.Length - 1)))
		{
			attemptNumber++;
			if (attemptNumber > 1)
			{
				GD.Print(mediaSourceName + " " + thingBeingRetrieved + " attempt " + attemptNumber);
			}

			try
			{
				await page.GoToAsync(uRLTarget, WaitUntilNavigation.Networkidle0);
			}
			catch (Exception exception)
			{
				if (exception.Message.Contains("Navigation timeout of 30000 ms exceeded"))
				{
					if (attemptNumber < maxAttempts)
					{
						continue;
					}
					else
					{
						GD.PushError(
							mediaSourceName + " appears to be down or blocking this traffic. Unable to connect in " +
							maxAttempts + " consecutive attempts."
						);
						return null;
					}
				}
				else
				{
					throw;
				}
			}

			var finalSelectorUsed = selector switch
			{
				"itemContainer vod-item-poster-atc" => "li[class$=\"" + selector + "\"]",
				_ => selector
			};

			try
			{
				var element = await page.WaitForSelectorAsync(finalSelectorUsed);
				await element.DisposeAsync();
			}
			catch (Exception exception)
			{
				if (
					!exception.Message.Contains("Waiting for selector `" + finalSelectorUsed + "` failed")
				)
				{
					GD.PushError(mediaSourceName + ": " + exception.Message);
					(Engine.GetMainLoop() as SceneTree).Quit();
				}

				pageHtml = await page.GetContentAsync();
				if (!pageHtml.Contains(selector))
				{
					if (attemptNumber < maxAttempts)
					{
						continue;
					}
					else
					{
						await File.WriteAllTextAsync(
							Path.Combine(ViewModel.PathCache, mediaSourceName + " selector.txt"), pageHtml
						);
						GD.PushError(
							"Can't get " + selector + " from " + uRLTarget + " in " + maxAttempts + " consecutive attempts. Please " +
							"confirm this selector exists on the page, which has been logged."
						);
						return null;
					}
				}
				else
				{
					await File.WriteAllTextAsync(
						Path.Combine(ViewModel.PathCache, mediaSourceName + " selector.txt"), pageHtml
					);
					GD.PushError(
						mediaSourceName + ": the text you are looking for is present in the page at " + uRLTarget + ", " +
						"but the CSS selector you used, " + finalSelectorUsed + ", is invalid and won't return a match. " +
						"Use https://gist.github.com/megclaypool/6d6bcf52d5e32a13a63d24ead83b4c83 as a reference to help " +
						"you correct your selector. The page you were loading has been logged for your reference."
					);
				}
			}

			pageHtml = await page.GetContentAsync();
		}
			
		return pageHtml;
	}
}