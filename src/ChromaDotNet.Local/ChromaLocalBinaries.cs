using System.Runtime.InteropServices;

namespace ChromaDotNet.Local;

// The executable of the engine and, on Linux and macOS, the guard of the base, in this order:
// - ExecutablePath, with the guard next to it or in the base package;
// - the runtime package of the engine for this platform, which NuGet copies to runtimes/<rid>/native/ for an application for every
//   platform, and next to the application when it is published for one;
// - the cache of the user, downloaded from the release of the engine when it is not there (ExecutableCache).
// The files of the packages are looked for next to the application, next to this assembly, where a host of plugins, like PowerShell,
// loads it with the files of its packages, and in the directories of the native libraries of the application, which in a single file
// with them inside have the directory they are extracted to. The guard is in the base package, in the same places.
internal sealed class ChromaLocalBinaries
{
	public const string GuardName = "chromadotnet-local-guard";

	public string ChromaPath { get; }

	public string? GuardPath { get; }

	// The engine package, or null for an ExecutablePath without one.
	public EnginePackage? Engine { get; }

	internal ChromaLocalBinaries(string chromaPath, string? guardPath, EnginePackage? engine = null)
	{
		ChromaPath = chromaPath;
		GuardPath = guardPath;
		Engine = engine;
	}

	// The engine is the one of EnginePackage.Resolve, null only with an ExecutablePath.
	public static async Task<ChromaLocalBinaries> ResolveAsync(ChromaLocalOptions options, EnginePackage? engine, CancellationToken cancellationToken)
	{
		var rids = RuntimeIdentifiers().ToList();
		var roots = new[] { AppContext.BaseDirectory, AssemblyDirectory() }.OfType<string>().Select(FullPath).Distinct(PathComparer).ToList();
		// The files of a runtime package in runtimes/<rid>/native/; then the directories of the files of a publish for one platform.
		var flat = roots.Concat(NativeSearchDirectories()).Distinct(PathComparer).ToList();
		var directories = roots.SelectMany(root => rids.Select(rid => Path.Combine(root, "runtimes", rid, "native"))).Concat(flat).ToList();

		string? chromaPath = null;
		// The platform of the runtime package the executable comes from: runtimes/<rid>/native/, or the first one for the files next to
		// the application.
		string? packageRid = null;
		if (options.ExecutablePath is not null)
		{
			chromaPath = Path.GetFullPath(options.ExecutablePath);
			if (!File.Exists(chromaPath))
				throw new FileNotFoundException($"No executable at {chromaPath}.", chromaPath);
			directories.Insert(0, Path.GetDirectoryName(chromaPath)!);
		}
		else
		{
			foreach (var rid in rids)
			{
				if (engine!.ExecutableName(rid) is not { } name)
					continue;
				chromaPath = roots.Select(root => Path.Combine(root, "runtimes", rid, "native", name)).FirstOrDefault(File.Exists)
					?? flat.Select(d => Path.Combine(d, name)).FirstOrDefault(File.Exists);
				if (chromaPath is not null)
				{
					packageRid = rid;
					break;
				}
			}
		}

		var guardPath = Platform.IsWindows ? null : directories.Select(d => Path.Combine(d, GuardName)).FirstOrDefault(File.Exists);
		if (chromaPath is null)
		{
			// No runtime package: the executable of the release of the engine, for the first platform it has one for.
			var rid = rids.FirstOrDefault(r => engine!.Hash(r) is not null && engine.AssetName(r) is not null && engine.ExecutableName(r) is not null);
			if (!options.DownloadExecutable || rid is null)
				throw NoExecutable(engine!, options, directories);
			chromaPath = await ExecutableCache.GetEngineAsync(engine!, rid, options, cancellationToken).ConfigureAwait(false);
		}

		if (!Platform.IsWindows)
		{
			// Another user owns the files, with the mode 744 NuGet gives them, as in a container published as root and started as another
			// user: copies in the cache of this user run, the engine checked against the hash of its package, the guard against that of
			// the base package.
			var engineBlocked = !Platform.TryMakeExecutable(chromaPath);
			var guardBlocked = guardPath is not null && !Platform.TryMakeExecutable(guardPath);
			if (engineBlocked || guardBlocked)
			{
				var files = new[] { engineBlocked ? chromaPath : null, guardBlocked ? guardPath : null }.OfType<string>().ToArray();
				var guardRid = rids.FirstOrDefault(r => ExecutableCache.GuardHash(r) is not null);
				if ((engineBlocked && (options.ExecutablePath is not null || packageRid is null || engine!.Hash(packageRid) is null)) || (guardBlocked && guardRid is null))
					throw NotExecutable(files);
				try
				{
					if (engineBlocked)
						chromaPath = await ExecutableCache.GetEngineAsync(engine!, packageRid!, options, cancellationToken, chromaPath).ConfigureAwait(false);
					if (guardBlocked)
						guardPath = await ExecutableCache.GetGuardAsync(guardRid!, guardPath!, options, cancellationToken).ConfigureAwait(false);
				}
				catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is InvalidDataException)
				{
					throw NotExecutable(files, e);
				}
			}
		}
		return new ChromaLocalBinaries(chromaPath, guardPath, engine);
	}

	// The paths compare as the file system of the platform does.
	private static StringComparer PathComparer { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

	// The directory this assembly was loaded from; none in a single file, where it is inside the bundle.
#if NET
	[System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file",
		Justification = "Empty in a single file: then there is no directory to look in, and the native search directories have the files.")]
#endif
	private static string? AssemblyDirectory()
	{
		var location = typeof(ChromaLocalBinaries).Assembly.Location;
		return string.IsNullOrEmpty(location) ? null : Path.GetDirectoryName(location);
	}

	// The directories where the runtime looks for the native libraries of the application. In a single file with the native libraries
	// inside, the first is the directory they are extracted to. None on .NET Framework.
	private static IEnumerable<string> NativeSearchDirectories() =>
		AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") is string directories
			? directories.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries).Where(Path.IsPathRooted).Select(FullPath)
			: [];

	// The full path of a directory without the separator at the end, but for a root, as C:\ or /.
	private static string FullPath(string directory)
	{
		var full = Path.GetFullPath(directory);
		var trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		return trimmed.Length > (Path.GetPathRoot(full)?.Length ?? 0) ? trimmed : full;
	}

	private static PlatformNotSupportedException NoExecutable(EnginePackage engine, ChromaLocalOptions options, List<string> directories)
	{
		var where = engine.Engine == ChromaLocalEngine.ChromaDB
			? $"The runtime packages of {engine.PackageId} have the executables of win-x64, linux-x64, linux-arm64, osx-x64 and osx-arm64, " +
				"unless ChromaDotNetLocalChromaDBDownload takes them out of the output; then the start downloads them"
			: $"{engine.PackageId} downloads the executables of win-x64, linux-x64, linux-arm64, osx-x64 and osx-arm64 at the first start";
		return new PlatformNotSupportedException(
			$"No {engine.DisplayName} executable for {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}. {where}" +
			(options.DownloadExecutable ? "" : ", with ChromaLocalOptions.DownloadExecutable") +
			$". Looked in: {string.Join(", ", directories)}. ExecutablePath starts another executable.");
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
