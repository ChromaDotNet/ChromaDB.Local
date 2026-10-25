using System.Reflection;

namespace ChromaDotNet.Local;

// An engine as its package describes it. The package has an assembly of its own, ChromaDotNet.Local.<engine>, with a type EngineInfo
// and no code: its AssemblyMetadata, with the prefix ChromaDotNet.Local., say the version of the server, where its executables are
// downloaded from and the directory of the cache for them, and for each platform the name of the executable, the name of its file in
// the release and its SHA-256 hash. Data and not code, so that a base and an engine of different versions work together.
internal sealed class EnginePackage
{
	private const string Prefix = "ChromaDotNet.Local.";
	private readonly Dictionary<string, string> _metadata;

	private EnginePackage(ChromaLocalEngine engine, Dictionary<string, string> metadata)
	{
		Engine = engine;
		_metadata = metadata;
	}

	public ChromaLocalEngine Engine { get; }

	// The server, in messages.
	public string DisplayName => DisplayNameOf(Engine);

	public string PackageId => PackageIdOf(Engine);

	public string Version => Required("EngineVersion");

	// The address of the files of the release, with a slash at the end.
	public Uri ReleaseUri => new(Required("ReleaseUri"));

	// The directory of the cache for the executables of this version, like chromadb-v1.5.9.1-preview.6.
	public string CacheName => Required("CacheName");

	// The name of the executable of a platform, in the runtime package and in the cache, like chromadb.exe; null when the engine has
	// none for the platform.
	public string? ExecutableName(string rid) => Value("Executable:" + rid);

	// The name of the file of the release, like chromadb-linux-x64.gz, compressed with gzip when it ends with .gz.
	public string? AssetName(string rid) => Value("Asset:" + rid);

	// The SHA-256 hash of the executable, uncompressed.
	public string? Hash(string rid) => Value("Sha256:" + rid)?.ToLowerInvariant();

	// The longest DataPath the server works with on Windows, where its index opens files without the support of long paths; null when
	// the package does not say it.
	public int? MaxWindowsDataPathLength => Value("MaxWindowsDataPathLength") is { } length
		? int.Parse(length, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture)
		: null;

	public static string DisplayNameOf(ChromaLocalEngine engine) => engine == ChromaLocalEngine.ChromaDB ? "Chroma" : "VecturaDB";

	public static string PackageIdOf(ChromaLocalEngine engine) => engine switch
	{
		ChromaLocalEngine.ChromaDB => "ChromaDotNet.Local.ChromaDB",
		ChromaLocalEngine.VecturaDB => "ChromaDotNet.Local.VecturaDB",
		_ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null),
	};

	// The engine package of the application, or null when it does not have it. The names are constants, so that trimming and
	// NativeAOT keep the type, and the assembly with its metadata, when the application references the package.
	public static EnginePackage? Find(ChromaLocalEngine engine)
	{
		var type = engine switch
		{
			ChromaLocalEngine.ChromaDB => Type.GetType("ChromaDotNet.Local.ChromaDB.EngineInfo, ChromaDotNet.Local.ChromaDB", throwOnError: false),
			ChromaLocalEngine.VecturaDB => Type.GetType("ChromaDotNet.Local.VecturaDB.EngineInfo, ChromaDotNet.Local.VecturaDB", throwOnError: false),
			_ => throw new ArgumentOutOfRangeException(nameof(engine), engine, null),
		};
		if (type is null)
			return null;
		var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var attribute in type.Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
			if (attribute.Key.StartsWith(Prefix, StringComparison.Ordinal) && attribute.Value is not null)
				metadata[attribute.Key.Substring(Prefix.Length)] = attribute.Value;
		if (!metadata.TryGetValue("Engine", out var name) || name != engine.ToString())
			throw new InvalidOperationException($"The assembly {type.Assembly.FullName} does not describe the engine {engine}: reinstall the package {PackageIdOf(engine)}.");
		return new EnginePackage(engine, metadata);
	}

	// The engine of a start: the one of the options, or the only engine package of the application. Null only with an ExecutablePath
	// and no engine package, as the executable then needs none.
	public static EnginePackage? Resolve(ChromaLocalOptions options)
	{
		if (options.Engine is { } chosen)
		{
			return Find(chosen) ?? throw new InvalidOperationException(
				$"ChromaLocalOptions.Engine is {chosen}, and the application does not have the package {PackageIdOf(chosen)}: add it.");
		}
		var found = new[] { ChromaLocalEngine.ChromaDB, ChromaLocalEngine.VecturaDB }.Select(Find).OfType<EnginePackage>().ToList();
		if (found.Count == 1)
			return found[0];
		if (found.Count == 2)
		{
			throw new InvalidOperationException(
				"The application has two engines, ChromaDotNet.Local.ChromaDB and ChromaDotNet.Local.VecturaDB: set ChromaLocalOptions.Engine " +
				"to ChromaLocalEngine.ChromaDB or ChromaLocalEngine.VecturaDB.");
		}
		if (options.ExecutablePath is not null)
			return null;
		throw new InvalidOperationException(
			"ChromaDotNet.Local has no engine. Add ChromaDotNet.Local.ChromaDB for Chroma, or ChromaDotNet.Local.VecturaDB for VecturaDB.");
	}

	private string? Value(string key) => _metadata.TryGetValue(key, out var value) ? value : null;

	private string Required(string key) => Value(key)
		?? throw new InvalidOperationException($"The package {PackageId} does not have {Prefix}{key}: reinstall it.");
}
