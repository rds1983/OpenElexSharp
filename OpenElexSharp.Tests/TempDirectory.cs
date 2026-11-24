namespace OpenElexSharp.Tests;

internal sealed class TempDirectory : IDisposable
{
	public TempDirectory()
	{
		Root = Path.Combine(Path.GetTempPath(), "OpenElexSharp.Tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
	}

	public string Root { get; }

	public string CreateDirectory(string relativePath)
	{
		var path = Path.Combine(Root, relativePath);
		Directory.CreateDirectory(path);
		return path;
	}

	public string CreateFile(string relativePath, string content = "")
	{
		var path = Path.Combine(Root, relativePath);
		var parent = Path.GetDirectoryName(path);
		if (!string.IsNullOrEmpty(parent))
		{
			Directory.CreateDirectory(parent);
		}

		File.WriteAllText(path, content);
		return path;
	}

	public void Dispose()
	{
		try
		{
			Directory.Delete(Root, recursive: true);
		}
		catch (IOException)
		{
			// Best-effort cleanup of test data.
		}
		catch (UnauthorizedAccessException)
		{
			// Best-effort cleanup of test data.
		}
	}
}
