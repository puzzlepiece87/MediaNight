namespace MediaNight.C_.Tools;

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Environment = System.Environment;


internal static class ExtensionMethods
{
	extension(string original)
	{
		public string Left(int length)
		{
			if (length >= original.Length)
			{
				return original;
			}

			return original.Substring(0, length);
		}

		
		public string Mid(int startPosition, int length)
		{
			if (startPosition >= original.Length)
			{
				throw new ArgumentException(string.Join((string?)Environment.NewLine,
					"startPosition argument exceeds length of string.",
					"Method executed: Mid(" + original + ", " + startPosition + ", " + length + ")"
				), nameof(startPosition));
			}

			if (length >= original.Length - startPosition)
			{
				return original.Substring(startPosition, original.Length - startPosition);
			}

			return original.Substring(startPosition, length);
		}

		
		public string Right(int length)
		{
			if (length >= original.Length)
			{
				return original;
			}

			return original.Substring(original.Length - length);
		}

		
		public List<int> GetAllIndicesOfSubstring(string substring)
		{
			if (string.IsNullOrEmpty(substring))
			{
				throw new ArgumentException("substring argument of GetAllIndicesOfSubstring() may not be null or empty",
					nameof(substring));
			}

			var indices = new List<int>();

			var i = -1;
			do
			{
				i = original.IndexOf(substring, i + 1);

				if (i != -1)
				{
					indices.Add(i);
				}
			} while (i != -1);

			return indices;
		}

		
		public bool ContainsCaseInsensitive(string substring)
		{
			return original.Contains(substring, StringComparison.InvariantCultureIgnoreCase);
		}
	}


	public static void WriteToCSV(this DataTable dataTable, string cSVPath)
	{
		using var streamWriter = new StreamWriter(cSVPath);

		var headers = new List<string>();
		foreach (DataColumn column in dataTable.Columns)
		{
			headers.Add(column.ColumnName);
		}

		streamWriter.WriteLine(string.Join("|", headers));

		foreach (DataRow row in dataTable.Rows)
		{
			streamWriter.WriteLine(string.Join("|", row.ItemArray));
		}
	}
}