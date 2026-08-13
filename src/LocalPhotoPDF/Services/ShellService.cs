using System.Diagnostics;
using System.IO;

namespace LocalPhotoPDF.Services;

internal sealed class ShellService
{
    private readonly bool _useShellExecute = true;
    private readonly string _explorerExecutable = "explorer.exe";

    public void OpenFile(string filePath)
    {
        EnsureExistingFile(filePath);
        Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = _useShellExecute });
    }

    public void ShowInFolder(string filePath)
    {
        EnsureExistingFile(filePath);

        // Explorer only honours /select when the switch and the path are a SINGLE token joined
        // by the comma. ArgumentList quotes each entry independently and joins them with a
        // space, which produces `/select, "C:\...\out.pdf"` — Explorer cannot parse that and
        // silently opens the default folder instead of selecting the file, returning success
        // either way so nothing notices. Hence one explicit argument string.
        // This is safe to build by hand: a Windows path cannot contain a double quote, so there
        // is nothing to escape out of. The check below keeps that assumption honest.
        var fullPath = Path.GetFullPath(filePath);
        if (fullPath.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("A file path cannot contain a double quote.", nameof(filePath));
        }

        var startInfo = new ProcessStartInfo(_explorerExecutable)
        {
            Arguments = $"/select,\"{fullPath}\"",
            UseShellExecute = _useShellExecute,
        };
        Process.Start(startInfo);
    }

    private static void EnsureExistingFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The generated PDF is no longer at this location.", filePath);
        }
    }
}
