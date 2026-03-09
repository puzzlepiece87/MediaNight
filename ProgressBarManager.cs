namespace MediaNight.C_;

using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Godot;


public partial class ProgressBarManager : Node
{
	/// <summary>
	///     Manages the values, maximums, and labels of stacked displayed progress bars
	/// </summary>
	static ProgressBarManager()
	{
		ProgressBarManager.ProgressBarsByName = new ConcurrentDictionary<string, ProgressBarManager>();
		ProgressBarManager.ProgressBarNodesByName = new ConcurrentDictionary<string, ProgressBar>();
		ProgressBarManager.ProgressBarLabelsByName = new ConcurrentDictionary<string, Label>();
		ProgressBarManager.LabelSettings = new LabelSettings
		{
			FontSize = 28
		};
	}

	private static ConcurrentDictionary<string, ProgressBarManager> ProgressBarsByName { get; }
	private static ConcurrentDictionary<string, ProgressBar> ProgressBarNodesByName { get; }
	private static ConcurrentDictionary<string, Label> ProgressBarLabelsByName { get; }
	private static LabelSettings LabelSettings { get; }
	public static int Count => ProgressBarManager.ProgressBarsByName.Count;
	private int ProgressBarValue { get; set; }
	private int ProgressBarMaximum { get; set; }
	private string? TaskText { get; set; }
	private string LabelText =>
		this.TaskText + ": " + this.ProgressBarValue + " / " + this.ProgressBarMaximum;
	private Lock ProgressBarLock { get; } = new Lock();


	/// <param name="displayedTaskText">Describes the task being completed while the user waits</param>
	/// <param name="progressBarName"></param>
	/// <param name="progressBarMaximum"></param>
	/// <exception cref="ArgumentException">Thrown if there is already a progress bar by that name</exception>
	public static void CreateNewProgressBar(
		string? displayedTaskText = "",
		string progressBarName = "Default",
		int progressBarMaximum = 0,
		[CallerMemberName] string callingMethodName = ""
	)
	{
		var sceneTree = Engine.GetMainLoop() as SceneTree;

		if (!sceneTree.CurrentScene.SceneFilePath.Equals("res://ProgressBar.tscn"))
		{
			GD.PushError("ProgressBar scene is not open");
			sceneTree.Quit();
		}
		
		var progressBarManager = new ProgressBarManager();
		
		if (!ProgressBarManager.ProgressBarsByName.TryAdd(progressBarName, progressBarManager))
		{
			GD.PushError("There is already a progress bar by name " + progressBarName);
			sceneTree.Quit();
		}

		progressBarManager.ProgressBarMaximum = progressBarMaximum;
		progressBarManager.TaskText = displayedTaskText is null ? callingMethodName : displayedTaskText;
		
		var gridContainer = sceneTree.Root.GetNode("MarginContainer/GridContainer") as GridContainer;
		var label = new Label();
		label.Text = progressBarManager.TaskText;
		label.LabelSettings = ProgressBarManager.LabelSettings;
		var screenSize = DisplayServer.ScreenGetSize();
		label.SetCustomMinimumSize(new Vector2(screenSize.X * 0.55f, screenSize.Y * 0.01f));
		gridContainer.CallDeferred("add_child", label);
		var progressBar = new ProgressBar();
		progressBar.AddThemeFontSizeOverride("font_size", 28);
		gridContainer.CallDeferred("add_child", progressBar);
		
		ProgressBarManager.ProgressBarNodesByName.TryAdd(progressBarName, progressBar);
		ProgressBarManager.ProgressBarLabelsByName.TryAdd(progressBarName, label);
	}


	/// <summary>
	///     Increases the value of the progress bar named in the argument
	/// </summary>
	/// <param name="progressBarName">Specify a progress bar name other than 'Default'</param>
	/// <param name="amountToIncrement">Change the progress bar value by values other than 1</param>
	/// <exception cref="ArgumentException">Thrown if no progress bar is found by that name</exception>
	public static void IncrementValue(string progressBarName = "Default", int amountToIncrement = 1)
	{
		if (!ProgressBarManager.ProgressBarsByName.TryGetValue(progressBarName, out var progressBar))
		{
			GD.PushError("No progress bar exists by name " + progressBarName);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		lock (progressBar.ProgressBarLock)
		{
			progressBar.ProgressBarValue += amountToIncrement;

			ProgressBarManager.ProgressBarNodesByName[progressBarName].CallDeferred(
				"set_value", progressBar.ProgressBarValue
			);
		}
		
		ProgressBarManager.SetDisplayedTaskText(progressBar.TaskText, progressBarName);
	}


	/// <summary>
	///     Increases the maximum of the progress bar named in the argument
	/// </summary>
	/// <param name="progressBarName">Specify a progress bar name other than 'Default'</param>
	/// <param name="amountToIncrement">Change the progress bar maximum by values other than 1</param>
	/// <exception cref="ArgumentException">Thrown if no progress bar is found by that name</exception>
	public static void IncrementMaximum(int amountToIncrement = 1, string progressBarName = "Default")
	{
		if (!ProgressBarManager.ProgressBarsByName.TryGetValue(progressBarName, out var progressBar))
		{
			GD.PushError("No progress bar exists by name " + progressBarName);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		lock (progressBar.ProgressBarLock)
		{
			progressBar.ProgressBarMaximum += amountToIncrement;

			ProgressBarManager.ProgressBarNodesByName[progressBarName].CallDeferred(
				"set_max", progressBar.ProgressBarMaximum
			);
		}
	}


	/// <summary>
	///     Sets the task displayed in Label content of the progress bar named in the argument
	/// </summary>
	/// <param name="displayedTaskText">Description of the task being completed while the user waits</param>
	/// <param name="progressBarName">Specify a progress bar name other than 'Default'</param>
	/// <exception cref="ArgumentException">Thrown if no progress bar is found by that name</exception>
	public static void SetDisplayedTaskText(
		string displayedTaskText,
		string progressBarName = "Default"
	)
	{
		if (!ProgressBarManager.ProgressBarsByName.TryGetValue(progressBarName, out var progressBar))
		{
			GD.PushError("No progress bar exists by name " + progressBarName);
			(Engine.GetMainLoop() as SceneTree).Quit();
		}

		progressBar.TaskText = displayedTaskText;
		
		ProgressBarManager.ProgressBarLabelsByName[progressBarName].CallDeferred(
			"set_text", progressBar.LabelText
		);
	}
}