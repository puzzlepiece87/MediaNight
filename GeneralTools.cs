namespace MediaNight.C_.Tools;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using Godot;


public class GeneralTools
{
	public static (int, string, string) GetExitCodeOutputAndErrorOfSilentBackgroundCommand(
		string command, List<string> arguments, Delegate? methodThatUsesOutput, List<dynamic>? methodArguments
	)
	{
		var process = new Process();
		process.StartInfo.CreateNoWindow = false;
		process.StartInfo.WorkingDirectory = ViewModel.PathSavedMedia;
		process.StartInfo.FileName = command;
		foreach (var argument in arguments)
		{
			process.StartInfo.ArgumentList.Add(argument);
		}
		process.StartInfo.UseShellExecute = false;
		process.StartInfo.RedirectStandardOutput = true;
		if (methodThatUsesOutput is not null)
		{
			process.OutputDataReceived += (object sender, DataReceivedEventArgs ytDlpMessage) =>
				methodThatUsesOutput.DynamicInvoke(arguments, ytDlpMessage.Data);
		}
		process.StartInfo.RedirectStandardError = true;
		process.Start();
				
		var standardOutput = process.StandardOutput.ReadToEnd();
		Console.WriteLine(standardOutput);
		GD.Print(standardOutput);
		process.WaitForExit();
		var standardError = process.StandardError.ReadToEnd();
		Console.WriteLine(standardError);
		GD.Print(standardError);
		process.WaitForExit();
		
		return (process.ExitCode, standardOutput, standardError);
	}
}