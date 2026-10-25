using ChromaDB.Client;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDotNet.Local.Tests;

// The engines of an application that has both, as the project of these tests.
[TestFixture]
public class EngineTests
{
	private static readonly string[] Rids = ["win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64"];

	private string _root = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "chromadotnet-local-engine-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
	}

	[TearDown]
	public void TearDown()
	{
		try
		{
			Directory.Delete(_root, recursive: true);
		}
		catch (IOException)
		{
		}
	}

	// The two engines and no choice: an error that says how to choose, before anything starts. No engine wins on its own.
	[Test]
	public void TwoEnginesWithoutAChoice()
	{
		var path = Path.Combine(_root, "data");
		var e = Assert.ThrowsAsync<InvalidOperationException>(() => ChromaLocalServer.StartAsync(new() { DataPath = path }));
		Assert.That(e!.Message, Does.Contain("ChromaDotNet.Local.ChromaDB and ChromaDotNet.Local.VecturaDB").And.Contain("ChromaLocalOptions.Engine"));
		Assert.That(Directory.Exists(path), Is.False, "nothing started");
#pragma warning disable CS0618 // The property of the versions before 1.5.9.1-preview.6: with two engines, it has no version to say.
		Assert.That(() => ChromaLocalServer.ChromaVersion, Throws.InvalidOperationException.With.Message.Contains("ChromaLocalOptions.Engine"));
#pragma warning restore CS0618
	}

	// What each engine package says of itself: its version, the release of its executables, and the executable of each platform.
	[TestCase(ChromaLocalEngine.ChromaDB)]
	[TestCase(ChromaLocalEngine.VecturaDB)]
	public void EnginePackageDescribesItself(ChromaLocalEngine engine)
	{
		var package = EnginePackage.Find(engine)!;
		Assert.That(package.Engine, Is.EqualTo(engine));
		Assert.That(package.Version, Is.EqualTo(TestMetadata.EngineVersion(engine)));
		var release = engine == ChromaLocalEngine.ChromaDB
			? $"https://github.com/ChromaDotNet/ChromaDB.Local/releases/download/chromadb-v{TestMetadata.Value("ChromaDBVersion")}/"
			: $"https://github.com/VecturaDB/VecturaDB/releases/download/{TestMetadata.Value("VecturaDBTag")}/";
		Assert.That(package.ReleaseUri, Is.EqualTo(new Uri(release)));
		foreach (var rid in Rids)
		{
			var name = TestMetadata.ProcessName(engine) + (rid.StartsWith("win-", StringComparison.Ordinal) ? ".exe" : "");
			Assert.That(package.ExecutableName(rid), Is.EqualTo(name), rid);
			Assert.That(package.AssetName(rid), Is.Not.Null.And.Not.Empty, rid);
			Assert.That(package.Hash(rid), Does.Match("^[0-9a-f]{64}$"), rid);
		}
		Assert.That(package.ExecutableName("linux-musl-x64"), Is.Null);
		// Measured on Windows: Chroma 1.5.9 loads its index again with 206 characters, VecturaDB writes it with 200.
		Assert.That(package.MaxWindowsDataPathLength, Is.EqualTo(engine == ChromaLocalEngine.ChromaDB ? 206 : 200));
	}

	// One DataPath, three starts: Chroma writes it, VecturaDB reads it and writes a collection with sparse vectors, Chroma reads it
	// again. Chroma 1.5.9 has no sparse vectors: it reads the records of that collection, without them.
	[Test]
	public async Task DataOfOneEngineOpensWithTheOther()
	{
		var path = Path.Combine(_root, "shared");
		await using (var chroma = await ChromaLocalServer.StartAsync(new() { Engine = ChromaLocalEngine.ChromaDB, DataPath = path }))
		{
			using var client = chroma.CreateClient();
			var dense = client.GetCollectionClient(await client.CreateCollectionAsync("dense"));
			await dense.AddAsync(["a", "b"], [new([1f, 0f]), new([0f, 1f])], documents: ["first", "second"]);
		}
		await using (var vectura = await ChromaLocalServer.StartAsync(new() { Engine = ChromaLocalEngine.VecturaDB, DataPath = path }))
		{
			using var client = vectura.CreateClient();
			var dense = client.GetCollectionClient(await client.GetCollectionAsync("dense"));
			Assert.That(await dense.CountAsync(), Is.EqualTo(2), "VecturaDB reads the records of Chroma");
			Assert.That((await dense.QueryAsync(new ReadOnlyMemory<float>([1f, 0f]), nResults: 1))[0].Id, Is.EqualTo("a"));
			await dense.AddAsync(["c"], [new([1f, 1f])], documents: ["third"]);
			var sparse = client.GetCollectionClient(await client.CreateCollectionAsync(new ChromaCollectionDefinition("sparse")
			{
				Schema = new ChromaCollectionSchema().WithSparseVectorIndex("sparse", sourceKey: null, bm25: false),
			}));
			await sparse.AddAsync(new ChromaRecords(["s1"])
			{
				Embeddings = [new([1f, 0f])],
				Metadatas = [new Dictionary<string, object> { ["sparse"] = new ChromaSparseVector([1, 7], [1.0f, 0.5f]), ["k"] = 1 }],
			});
		}
		await using (var chroma = await ChromaLocalServer.StartAsync(new() { Engine = ChromaLocalEngine.ChromaDB, DataPath = path }))
		{
			using var client = chroma.CreateClient();
			var dense = client.GetCollectionClient(await client.GetCollectionAsync("dense"));
			Assert.That(await dense.CountAsync(), Is.EqualTo(3), "Chroma reads the records of VecturaDB");
			Assert.That((await dense.QueryAsync(new ReadOnlyMemory<float>([1f, 1f]), nResults: 1))[0].Id, Is.EqualTo("c"));
			var sparse = client.GetCollectionClient(await client.GetCollectionAsync("sparse"));
			var records = await sparse.GetAsync(include: ChromaGetInclude.Metadatas);
			Assert.That(records, Has.Count.EqualTo(1));
			Assert.That(records[0].Id, Is.EqualTo("s1"));
			Assert.That(records[0].Metadata!["k"], Is.EqualTo(1));
			Assert.That(records[0].Metadata!.ContainsKey("sparse"), Is.False, "Chroma 1.5.9 does not read the sparse vector");
		}
	}
}
