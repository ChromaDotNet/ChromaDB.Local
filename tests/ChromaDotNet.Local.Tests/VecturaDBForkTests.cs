using System.Globalization;
using ChromaDB.Client;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDotNet.Local.Tests;

// The fork of a collection, which VecturaDB has and Chroma 1.5.9 on one node does not, through ChromaDotNet.Client: what Chroma Cloud
// does with the same calls. The records and the metadata of the collection are copied, the writes after the fork stay on their side,
// the fork count is that of the lineage, and a fork to a name that exists fails.
[TestFixture]
public class VecturaDBForkTests
{
	private static readonly string[] SourceAfterItsWrite = ["fox1", "ml", "pie", "source_only"];
	private static readonly string[] ForkAfterItsDelete = ["fox1", "pie"];

	private ChromaLocalServer _server = null!;
	private ChromaClient _client = null!;

	[OneTimeSetUp]
	public async Task StartAsync()
	{
		_server = await ChromaLocalServer.StartAsync(new() { Engine = ChromaLocalEngine.VecturaDB });
		_client = _server.CreateClient();
	}

	[OneTimeTearDown]
	public async Task StopAsync()
	{
		_client.Dispose();
		await _server.DisposeAsync();
	}

	[Test]
	public async Task ForkOfACollection()
	{
		var source = _client.GetCollectionClient(await _client.CreateCollectionAsync(new ChromaCollectionDefinition("fork_source")
		{
			Metadata = new Dictionary<string, object> { ["owner"] = "tests", ["n"] = 7 },
		}));
		await source.AddAsync(new ChromaRecords(["fox1", "ml", "pie"])
		{
			Embeddings = [new([1f, 0f, 0f]), new([0f, 1f, 0f]), new([0f, 0f, 1f])],
			Documents = ["The quick brown fox", "Machine learning", "Apple pie"],
			Metadatas =
			[
				new Dictionary<string, object> { ["topic"] = "animals", ["sparse"] = new ChromaSparseVector([1, 7], [1.0f, 0.5f]) },
				new Dictionary<string, object> { ["topic"] = "tech" },
				new Dictionary<string, object> { ["topic"] = "food" },
			],
		});
		var include = ChromaGetInclude.Documents | ChromaGetInclude.Embeddings | ChromaGetInclude.Metadatas;

		await source.ForkAsync("fork_a");
		var fork = _client.GetCollectionClient(await _client.GetCollectionAsync("fork_a"));
		var copied = await fork.GetAsync(include: include);
		Assert.That(copied, Has.Count.EqualTo(3));
		Assert.That(Dump(copied), Is.EqualTo(Dump(await source.GetAsync(include: include))), "the records, with the sparse vector");
		var metadata = (await _client.GetCollectionAsync("fork_a")).Metadata!;
		Assert.That(metadata["owner"], Is.EqualTo("tests"));
		Assert.That(Convert.ToInt32(metadata["n"], CultureInfo.InvariantCulture), Is.EqualTo(7));
		Assert.That(await source.ForkCountAsync(), Is.EqualTo(1));
		Assert.That(await fork.ForkCountAsync(), Is.EqualTo(1));

		await source.AddAsync(new ChromaRecords(["source_only"]) { Embeddings = [new([0.5f, 0.5f, 0f])] });
		await fork.DeleteAsync(["ml"]);
		Assert.That((await source.GetAsync()).Select(r => r.Id).Order(), Is.EqualTo(SourceAfterItsWrite));
		Assert.That((await fork.GetAsync()).Select(r => r.Id).Order(), Is.EqualTo(ForkAfterItsDelete));

		await fork.ForkAsync("fork_b");
		var forkOfTheFork = _client.GetCollectionClient(await _client.GetCollectionAsync("fork_b"));
		Assert.That(await source.ForkCountAsync(), Is.EqualTo(2));
		Assert.That(await forkOfTheFork.ForkCountAsync(), Is.EqualTo(2));
		var e = Assert.ThrowsAsync<ChromaException>(() => source.ForkAsync("fork_a"));
		Assert.That(e!.Message, Does.Contain("Collection [fork_a] already exists"));
	}

	// Each record with its document, its embedding and its metadata, the sparse vectors included, in the order of the ids.
	private static string Dump(IReadOnlyList<ChromaCollectionEntry> rows) => string.Join("; ", rows.OrderBy(r => r.Id, StringComparer.Ordinal).Select(r =>
		$"{r.Id}|{r.Document}|{string.Join(",", r.Embedding?.ToArray() ?? [])}|" + string.Join(",", (r.Metadata ?? new Dictionary<string, object>())
			.OrderBy(k => k.Key, StringComparer.Ordinal)
			.Select(k => $"{k.Key}={(k.Value is ChromaSparseVector v ? string.Join("/", v.Indices.ToArray()) + ":" + string.Join("/", v.Values.ToArray()) : k.Value)}"))));
}
