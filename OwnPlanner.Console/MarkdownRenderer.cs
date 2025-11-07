using Spectre.Console;
using System.Text.RegularExpressions;

namespace OwnPlanner.Console
{
	/// <summary>
	/// Renders Markdown text to the console with formatting using Spectre.Console
	/// </summary>
	public static class MarkdownRenderer
	{
		public static void Render(string markdown)
		{
			if (string.IsNullOrWhiteSpace(markdown))
			{
				return;
			}

			try
			{
				RenderMarkdown(markdown);
			}
			catch (Exception ex)
			{
				// If rendering fails (e.g., malformed markup), fall back to raw output
				System.Console.WriteLine("[Warning: Markdown rendering failed, showing raw output]");
				System.Console.WriteLine($"[Error: {ex.Message}]");
				System.Console.WriteLine();
				System.Console.WriteLine(markdown);
			}
		}

		private static void RenderMarkdown(string markdown)
		{
			var lines = markdown.Split('\n');
			var inCodeBlock = false;
			var codeBlockLanguage = "";
			var codeBlockContent = new List<string>();
			var inList = false;
			var inTable = false;
			var tableLines = new List<string>();

			for (int i = 0; i < lines.Length; i++)
			{
				var line = lines[i];

				// Handle code blocks
				if (line.TrimStart().StartsWith("```"))
				{
					if (!inCodeBlock)
					{
						// Starting code block
						inCodeBlock = true;
						codeBlockLanguage = line.TrimStart().Substring(3).Trim();
						codeBlockContent.Clear();
					}
					else
					{
						// Ending code block
						inCodeBlock = false;
						RenderCodeBlock(codeBlockContent, codeBlockLanguage);
						codeBlockLanguage = "";
					}
					continue;
				}

				if (inCodeBlock)
				{
					codeBlockContent.Add(line);
					continue;
				}

				// Detect table start (line with | followed by separator line)
				if (!inTable && line.TrimStart().Contains('|') && i + 1 < lines.Length)
				{
					var nextLine = lines[i + 1];
					// Check if next line is a separator (contains | and - and optionally :)
					if (nextLine.Contains('|') && nextLine.Contains('-') && 
					    Regex.IsMatch(nextLine, @"^\s*\|?[\s\-:|]+\|[\s\-:|]*$"))
					{
						inTable = true;
						tableLines.Clear();
						tableLines.Add(line);
						continue;
					}
				}

				// Collect table lines
				if (inTable)
				{
					if (line.TrimStart().Contains('|'))
					{
						tableLines.Add(line);
						continue;
					}
					else
					{
						// End of table
						inTable = false;
						RenderTable(tableLines);
						tableLines.Clear();
						// Process current line normally (fall through)
					}
				}

				// Reset list flag if line doesn't start with list marker
				if (!line.TrimStart().StartsWith("-") && !line.TrimStart().StartsWith("*") && !Regex.IsMatch(line.TrimStart(), @"^\d+\."))
				{
					if (inList && !string.IsNullOrWhiteSpace(line))
					{
						inList = false;
					}
				}

				// Handle headers
				if (line.StartsWith("# "))
				{
					SafeMarkupLine($"[bold yellow]{EscapeMarkup(line.Substring(2))}[/]");
					continue;
				}
				if (line.StartsWith("## "))
				{
					SafeMarkupLine($"[bold cyan]{EscapeMarkup(line.Substring(3))}[/]");
					continue;
				}
				if (line.StartsWith("### "))
				{
					SafeMarkupLine($"[bold blue]{EscapeMarkup(line.Substring(4))}[/]");
					continue;
				}
				if (line.StartsWith("#### "))
				{
					SafeMarkupLine($"[bold]{EscapeMarkup(line.Substring(5))}[/]");
					continue;
				}

				// Handle lists
				var listMatch = Regex.Match(line, @"^(\s*)([-*]|\d+\.)\s+(.+)$");
				if (listMatch.Success)
				{
					inList = true;
					var indent = listMatch.Groups[1].Value;
					var marker = listMatch.Groups[2].Value;
					var content = listMatch.Groups[3].Value;
					var formattedContent = FormatInlineMarkdown(content);
					SafeMarkupLine($"{indent}[dim]{marker}[/] {formattedContent}");
					continue;
				}

				// Handle blockquotes
				if (line.TrimStart().StartsWith(">"))
				{
					var quoted = line.TrimStart().Substring(1).Trim();
					SafeMarkupLine($"[dim]? {EscapeMarkup(quoted)}[/]");
					continue;
				}

				// Handle horizontal rules
				if (line.Trim() == "---" || line.Trim() == "***" || line.Trim() == "___")
				{
					AnsiConsole.Write(new Rule().RuleStyle("dim"));
					continue;
				}

				// Handle inline code and formatting
				if (!string.IsNullOrWhiteSpace(line))
				{
					var formattedLine = FormatInlineMarkdown(line);
					SafeMarkupLine(formattedLine);
				}
				else
				{
					AnsiConsole.WriteLine();
				}
			}

			// Handle unclosed code block
			if (inCodeBlock)
			{
				RenderCodeBlock(codeBlockContent, codeBlockLanguage);
			}

			// Handle unclosed table
			if (inTable && tableLines.Count > 0)
			{
				RenderTable(tableLines);
			}
		}

		private static void RenderTable(List<string> tableLines)
		{
			if (tableLines.Count < 2)
			{
				// Not a valid table, just render as text
				foreach (var line in tableLines)
				{
					AnsiConsole.MarkupLine(EscapeMarkup(line));
				}
				return;
			}

			try
			{
				// Parse header
				var headerLine = tableLines[0].Trim();
				var headers = ParseTableRow(headerLine);

				// Skip separator line (index 1)
				// Parse alignment from separator
				var separatorLine = tableLines[1].Trim();
				var alignments = ParseTableAlignment(separatorLine);

				// Create table
				var table = new Table();
				table.Border(TableBorder.Rounded);
				table.BorderStyle(new Style(foreground: Color.Grey));

				// Add columns with alignments
				for (int i = 0; i < headers.Count; i++)
				{
					var header = FormatInlineMarkdown(headers[i]);
					var justify = i < alignments.Count ? alignments[i] : Justify.Left;
					table.AddColumn(new TableColumn(header).Centered());
				}

				// Add rows
				for (int i = 2; i < tableLines.Count; i++)
				{
					var rowLine = tableLines[i].Trim();
					var cells = ParseTableRow(rowLine);
					
					// Format cells
					var formattedCells = cells.Select(c => FormatInlineMarkdown(c)).ToArray();
					
					// Pad if necessary
					while (formattedCells.Length < headers.Count)
					{
						formattedCells = formattedCells.Concat(new[] { "" }).ToArray();
					}

					table.AddRow(formattedCells.Take(headers.Count).ToArray());
				}

				AnsiConsole.Write(table);
				AnsiConsole.WriteLine();
			}
			catch
			{
				// If table parsing fails, render as plain text
				foreach (var line in tableLines)
				{
					AnsiConsole.MarkupLine(EscapeMarkup(line));
				}
			}
		}

		private static List<string> ParseTableRow(string line)
		{
			// Remove leading and trailing pipes
			line = line.Trim();
			if (line.StartsWith("|"))
				line = line.Substring(1);
			if (line.EndsWith("|"))
				line = line.Substring(0, line.Length - 1);

			// Split by pipe and trim
			return line.Split('|')
				.Select(cell => cell.Trim())
				.ToList();
		}

		private static List<Justify> ParseTableAlignment(string separatorLine)
		{
			var cells = ParseTableRow(separatorLine);
			var alignments = new List<Justify>();

			foreach (var cell in cells)
			{
				var trimmed = cell.Trim();
				bool startsWithColon = trimmed.StartsWith(":");
				bool endsWithColon = trimmed.EndsWith(":");

				if (startsWithColon && endsWithColon)
					alignments.Add(Justify.Center);
				else if (endsWithColon)
					alignments.Add(Justify.Right);
				else
					alignments.Add(Justify.Left);
			}

			return alignments;
		}

		private static void RenderCodeBlock(List<string> lines, string language)
		{
			var panel = new Panel(string.Join(Environment.NewLine, lines))
			{
				Header = new PanelHeader(string.IsNullOrEmpty(language) ? "Code" : language, Justify.Left),
				Border = BoxBorder.Rounded,
				BorderStyle = new Style(foreground: Color.Grey)
			};
			AnsiConsole.Write(panel);
		}

		private static string FormatInlineMarkdown(string text)
		{
			if (string.IsNullOrWhiteSpace(text))
			{
				return text;
			}

			try
			{
				// Escape existing markup first
				text = EscapeMarkup(text);

				// Handle inline code
				text = Regex.Replace(text, @"`([^`]+)`", "[grey on grey19]$1[/]");

				// Handle bold
				text = Regex.Replace(text, @"\*\*([^\*]+)\*\*", "[bold]$1[/]");
				text = Regex.Replace(text, @"__([^_]+)__", "[bold]$1[/]");

				// Handle italic
				text = Regex.Replace(text, @"\*([^\*]+)\*", "[italic]$1[/]");
				text = Regex.Replace(text, @"_([^_]+)_", "[italic]$1[/]");

				// Handle links (simplified - just show the text)
				text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "[link]$1[/]");

				return text;
			}
			catch
			{
				// If inline formatting fails, return escaped text
				return EscapeMarkup(text);
			}
		}

		private static string EscapeMarkup(string text)
		{
			if (string.IsNullOrEmpty(text))
			{
				return text;
			}
			return text.Replace("[", "[[").Replace("]", "]]");
		}

		private static void SafeMarkupLine(string markup)
		{
			try
			{
				AnsiConsole.MarkupLine(markup);
			}
			catch
			{
				// If markup rendering fails, output as plain text
				var plainText = markup
					.Replace("[[", "[")
					.Replace("]]", "]")
					.Replace("[bold]", "")
					.Replace("[/bold]", "")
					.Replace("[italic]", "")
					.Replace("[/italic]", "")
					.Replace("[dim]", "")
					.Replace("[/dim]", "")
					.Replace("[link]", "")
					.Replace("[/link]", "")
					.Replace("[bold yellow]", "")
					.Replace("[bold cyan]", "")
					.Replace("[bold blue]", "")
					.Replace("[/]", "");
				
				// Remove any remaining markup patterns
				plainText = Regex.Replace(plainText, @"\[grey on grey19\]([^\[]+)\[/\]", "`$1`");
				
				System.Console.WriteLine(plainText);
			}
		}
	}
}
