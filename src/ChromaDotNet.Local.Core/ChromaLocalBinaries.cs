using System.Runtime.InteropServices;

namespace ChromaDotNet.Local;

// The Chroma executable and, on Linux and macOS, the guard, in this order:
// - ExecutablePath, with the guard next to it or in a runtime package;
// - the ChromaDotNet.Local.Runtime package of this platform, which NuGet copies to runtimes/<rid>/native/ for an application for every
//   platform, and next to the application when it is published for one;
// - the cache of the user, downloaded from the GitHub release of this version when they are not there (ExecutableCache).
internal sealed class ChromaLocalBinaries
{
	public const string GuardName = "chroma-local-guard";

	public string ChromaPath { get; }

	public string? GuardPath { get; }

	private ChromaLocalBinaries(string chromaPath, string? guardPath)
	{
		ChromaPath = chromaPath;
		GuardPath = guardPath;
	}

	public static async Task<ChromaLocalBinaries> ResolveAsync(ChromaLocalOptions options, CancellationToken cancellationToken)
	{
		var chromaName = Platform.IsWindows ? "chroma.exe" : "chroma";
		var baseDirectory = AppContext.BaseDirectory;
		var rids = RuntimeIdentifiers().ToList();
		var directories = rids.Select(rid => Path.Combine(baseDirectory, "runtimes", rid, "native")).Append(baseDirectory).ToList();

		string? chromaPath;
		if (options.ExecutablePath is not null)
		{
			chromaPath = Path.GetFullPath(options.ExecutablePath);
			if (!File.Exists(chromaPath))
				throw new FileNotFoundException($"No Chroma executable at {chromaPath}.", chromaPath);
			directories.Insert(0, Path.GetDirectoryName(chromaPath)!);
		}
		else
		{
			chromaPath = directories.Select(d => Path.Combine(d, chromaName)).FirstOrDefault(File.Exists);
		}

		string? guardPath = null;
		if (chromaPath is null)
		{
			// No runtime package: the executables of the release, the first platform this build has hashes for.
			var rid = rids.FirstOrDefault(r => ExecutableCache.Hash(r, chromaName) is not null);
			if (!options.DownloadExecutable || rid is null)
				throw new PlatformNotSupportedException(
					$"No Chroma executable for {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}. Reference ChromaDotNet.Local, which brings the " +
					$"executables of every platform, or the ChromaDotNet.Local.Runtime package of this one: win-x64, linux-x64, linux-arm64, osx-x64 or osx-arm64" +
					(options.DownloadExecutable ? "" : ", or let ChromaLocalOptions.DownloadExecutable download it") +
					$". Looked in: {string.Join(", ", directories)}. ExecutablePath starts another Chroma executable.");
			var names = Platform.IsWindows ? new[] { chromaName } : new[] { chromaName, GuardName };
			var paths = await ExecutableCache.GetAsync(rid, names, options, cancellationToken).ConfigureAwait(false);
			chromaPath = paths[0];
			guardPath = paths.Length > 1 ? paths[1] : null;
		}
		else if (!Platform.IsWindows)
		{
			guardPath = directories.Select(d => Path.Combine(d, GuardName)).FirstOrDefault(File.Exists);
		}

		if (!Platform.IsWindows)
		{
			Platform.EnsureExecutable(chromaPath);
			if (guardPath is not null)
				Platform.EnsureExecutable(guardPath);
		}
		return new ChromaLocalBinaries(chromaPath, guardPath);
	}

	// The runtime identifiers to look for, best first. Windows on Arm runs the x64 executable through emulation.
	private static IEnumerable<string> RuntimeIdentifiers()
	{
		var architecture = RuntimeInformation.OSArchitecture switch
		{
			Architecture.X64 => "x64",
			Architecture.Arm64 => "arm64",
			_ => null,
		};
		if (architecture is null)
			yield break;
		if (Platform.IsWindows)
		{
			yield return "win-" + architecture;
			if (architecture == "arm64")
				yield return "win-x64";
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
		{
			yield return "linux-" + architecture;
		}
		else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
		{
			yield return "osx-" + architecture;
		}
	}
}
