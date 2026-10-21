#:package ChromaDotNet.Local@1.5.9.1-preview.5
#:package ChromaDotNet.Client@2.17.0
#:package ChromaDotNet.Embeddings@1.0.0-preview.3
#:property PublishAot=false

using ChromaDB.Client;
using ChromaDB.Client.Models;
using ChromaDotNet.Embeddings;
using ChromaDotNet.Local;

// As chromadb.PersistentClient(path="chroma-data") in Python: Chroma on this machine, with the data in chroma-data of the current
// directory, and the default embedding function of Chroma. No Docker, no Python.
await using var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = "chroma-data" });
Console.WriteLine($"Chroma {ChromaLocalServer.ChromaVersion}, data in {chroma.DataPath}");

// all-MiniLM-L6-v2, the model of Python: the first run downloads it, 83 MB, into the cache Python uses too.
using var embeddings = await ChromaDefaultEmbeddingGenerator.CreateAsync();
using var client = chroma.CreateClient();

// The collection declares the embedding function "default", as one Python creates: Python opens it, and the other way round.
var collection = await client.GetOrCreateCollectionAsync(new ChromaCollectionDefinition("fruits") { EmbeddingGenerator = embeddings });
var fruits = client.GetCollectionClient(collection).WithEmbeddingGenerator(embeddings);

var count = await fruits.CountAsync();
if (count == 0)
{
	// Only the documents: the client computes their embeddings.
	await fruits.AddAsync(["apple", "lemon", "banana"], documents: ["A crunchy apple", "A sour lemon", "A sweet banana"]);
	Console.WriteLine("Added 3 fruits. Run it again: they are still there.");
}
else
{
	Console.WriteLine($"{count} fruits from a previous run.");
}

// A text as the query: the client computes its embedding too.
var results = await fruits.QueryAsync("something sour", nResults: 2, include: ChromaQueryInclude.Documents | ChromaQueryInclude.Distances);
foreach (var result in results)
{
	Console.WriteLine($"{result.Id}: {result.Document}, distance {result.Distance}");
}
