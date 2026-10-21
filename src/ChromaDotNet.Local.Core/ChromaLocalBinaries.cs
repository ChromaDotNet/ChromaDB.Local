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

	internal ChromaLocalBinaries(string chromaPath, string? guardPath)
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
		// The platform of the runtime package the executable comes from: runtimes/<rid>/native/, or the first one for the files next to
		// the application.
		var packageRid = chromaPath is null ? null
			: rids.FirstOrDefault(r => string.Equals(Path.GetDirectoryName(chromaPath), Path.Combine(baseDirectory, "runtimes", r, "native"), StringComparison.Ordinal))
				?? rids.FirstOrDefault();

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
			var files = guardPath is null ? new[] { chromaPath } : new[] { chromaPath, guardPath };
			if (!files.All(Platform.TryMakeExecutable))
			{
				// Another user owns the files, with the mode 744 NuGet gives them, as in a container published as root and started as
				// another user: a copy in the cache of this user, checked against the hashes of this package, runs.
				var names = files.Select(Path.GetFileName).ToArray();
				if (options.ExecutablePath is not null || packageRid is null || names.Any(n => ExecutableCache.Hash(packageRid, n!) is null))
					throw NotExecutable(files);
				string[] copies;
				try
				{
					copies = await ExecutableCache.GetAsync(packageRid, names!, options, cancellationToken, files).ConfigureAwait(false);
				}
				catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is InvalidDataException)
				{
					throw NotExecutable(files, e);
				}
				chromaPath = copies[0];
				guardPath = copies.Length > 1 ? copies[1] : null;
			}
		}
		return new ChromaLocalBinaries(chromaPath, guardPath);
	}

	private static UnauthorizedAccessException NotExecutable(string[] files, Exception? copy = null) => new(
		$"This user cannot execute {string.Join(" and ", files)}, and cannot make them executable: another user owns them. Run chmod a+x on " +
		$"them, or copy them with COPY --chmod=755 in a Dockerfile" +
		(copy is null ? "." : $". Their copy into the cache of this user failed too: {copy.Message}"), copy);

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
