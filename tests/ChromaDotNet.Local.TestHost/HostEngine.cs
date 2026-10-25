using ChromaDotNet.Local;

// The engine of the variable CHROMA_LOCAL_ENGINE, ChromaDB or VecturaDB, which the build with the two engines needs; none without it,
// for the builds with one engine or none.
internal static class HostEngine
{
	public static ChromaLocalEngine? Chosen { get; } = Environment.GetEnvironmentVariable("CHROMA_LOCAL_ENGINE") switch
	{
		null or "" => null,
		"ChromaDB" => ChromaLocalEngine.ChromaDB,
		"VecturaDB" => ChromaLocalEngine.VecturaDB,
		var other => throw new InvalidOperationException($"CHROMA_LOCAL_ENGINE is {other}: ChromaDB or VecturaDB."),
	};
}
