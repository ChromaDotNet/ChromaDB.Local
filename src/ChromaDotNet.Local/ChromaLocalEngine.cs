namespace ChromaDotNet.Local;

/// <summary>
/// The engine a <see cref="ChromaLocalServer"/> runs. Each engine comes with a package of its own, on top of ChromaDotNet.Local.
/// </summary>
public enum ChromaLocalEngine
{
	/// <summary>
	/// Chroma, from the package ChromaDotNet.Local.ChromaDB.
	/// </summary>
	ChromaDB,

	/// <summary>
	/// VecturaDB, compatible with the Chroma API, from the package ChromaDotNet.Local.VecturaDB.
	/// </summary>
	VecturaDB,
}
