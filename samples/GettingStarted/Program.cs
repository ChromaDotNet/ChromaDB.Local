using ChromaDB.Client;
using ChromaDotNet.Local;

// Chroma starts as a child process of this application: no Docker, no Python.
// The data stays in chroma-data next to the application, so the next run finds the records of this one.
var dataPath = Path.Combine(AppContext.BaseDirectory, "chroma-data");
await using var chroma = await ChromaLocalServer.StartAsync(new() { DataPath = dataPath });
Console.WriteLine($"Chroma {ChromaLocalServer.ChromaVersion} at {chroma.Uri}");
Console.WriteLine($"Data in {chroma.DataPath}");

using var client = chroma.CreateClient();
var fruits = client.GetCollectionClient(await client.GetOrCreateCollectionAsync("fruits"));
Console.WriteLine($"Records before the upsert: {await fruits.CountAsync()}");

// Embeddings of three dimensions: sweet, sour, crunchy. A real application gets them from an embedding model.
float[] apple = [0.6f, 0.3f, 0.9f];
float[] lemon = [0.1f, 1.0f, 0.2f];
float[] banana = [0.9f, 0.1f, 0.1f];
await fruits.UpsertAsync(
	["apple", "lemon", "banana"],
	[apple, lemon, banana],
	documents: ["A crunchy apple", "A sour lemon", "A sweet banana"]);
Console.WriteLine($"Records after the upsert: {await fruits.CountAsync()}");

// The two fruits closest to something sour.
float[] sour = [0.2f, 0.9f, 0.1f];
var results = await fruits.QueryAsync(sour, nResults: 2, include: ChromaQueryInclude.Documents | ChromaQueryInclude.Distances);
foreach (var result in results)
{
	Console.WriteLine($"{result.Id}: {result.Document}, distance {result.Distance}");
}

// await using stops Chroma here. It also stops if the application ends without it, or is killed.
