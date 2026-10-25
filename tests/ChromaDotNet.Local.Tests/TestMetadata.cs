using System.Reflection;

namespace ChromaDotNet.Local.Tests;

// The AssemblyMetadata of the project of the tests: the directory of the test host, the version of the base, and what the engine
// packages say of themselves, from Directory.Build.props.
internal static class TestMetadata
{
	public static string Value(string key) => typeof(TestMetadata).Assembly
		.GetCustomAttributes<AssemblyMetadataAttribute>().First(a => a.Key == key).Value!;

	// The version of the server of an engine, as ChromaLocalServer.EngineVersion says it.
	public static string EngineVersion(ChromaLocalEngine engine) =>
		engine == ChromaLocalEngine.ChromaDB ? Value("ChromaVersion") : Value("VecturaDBTag").TrimStart('v');

	// The name of the process of an engine, as Process.GetProcessesByName finds it.
	public static string ProcessName(ChromaLocalEngine engine) => engine == ChromaLocalEngine.ChromaDB ? "chromadb" : "vecturadb";
}
