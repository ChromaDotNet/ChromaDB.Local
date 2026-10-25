using ChromaDB.Client;
using ChromaDB.Client.Models;
using NUnit.Framework;

namespace ChromaDotNet.Local.Tests;

// What VecturaDB has and Chroma 1.5.9 on one node does not: sparse vectors, BM25 and the Search API, through ChromaDotNet.Client. The
// data and the expected answers are those of Chroma Cloud after the indexing, for the same records and the same searches; the group by
// and its expected records are those of Chroma's own test_search_group_by. One difference in the data: the 300 records that the
// searches do not find are on distinct vectors, not on one point. On one point, the HNSW index of a single node, of VecturaDB as of
// Chroma 1.5.9, misses the records near the query in about two collections out of five, also without a where; the answers of Cloud
// do not depend on those vectors, as the where leaves those records out, and BM25 reads the documents.
[TestFixture]
public class VecturaDBSearchTests
{
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

	// Three records with sparse vectors, and 300 records without k with a sparse vector on another index.
	private async Task<ChromaCollectionClient> SparseCollectionAsync(string name)
	{
		var collection = _client.GetCollectionClient(await _client.CreateCollectionAsync(new ChromaCollectionDefinition(name)
		{
			Schema = new ChromaCollectionSchema().WithSparseVectorIndex("sparse", sourceKey: null, bm25: false),
		}));
		await collection.AddAsync(new ChromaRecords(["s1", "s2", "s3"])
		{
			Embeddings = [new([1f, 0f]), new([0f, 1f]), new([1f, 1f])],
			Documents = ["uno", "due", "tre"],
			Metadatas =
			[
				new Dictionary<string, object> { ["sparse"] = new ChromaSparseVector([1, 7], [1.0f, 0.5f]), ["k"] = 1 },
				new Dictionary<string, object> { ["sparse"] = new ChromaSparseVector([2, 9], [0.8f, 0.2f]), ["k"] = 2 },
				new Dictionary<string, object> { ["sparse"] = new ChromaSparseVector([1, 2], [0.3f, 0.3f]), ["k"] = 3 },
			],
		});
		var fillers = Enumerable.Range(0, 300).Select(i => $"f{i}").ToList();
		await collection.AddAsync(new ChromaRecords(fillers)
		{
			Embeddings = fillers.Select((_, i) => new ReadOnlyMemory<float>([0.5f, 0.3f + i / 1000f])).ToList(),
			Documents = fillers.Select((_, i) => $"filler record number {i} about weather and trains").ToList(),
			Metadatas = fillers.Select(_ => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["topic"] = "tech", ["sparse"] = new ChromaSparseVector([50], [0.1f]) }).ToList(),
		});
		return collection;
	}

	// Four documents with BM25 on the document, and 300 others without the words of the searches.
	private async Task<ChromaCollectionClient> Bm25CollectionAsync(string name)
	{
		var collection = _client.GetCollectionClient(await _client.CreateCollectionAsync(new ChromaCollectionDefinition(name)
		{
			Schema = new ChromaCollectionSchema().WithSparseVectorIndex("doc_bm25", ChromaSearchKeys.Document, bm25: true, new ChromaBm25().Reference),
		}));
		await collection.AddAsync(new ChromaRecords(["fox1", "fox2", "ml", "pie"])
		{
			Embeddings = [new([1f, 0f, 0f]), new([0.9f, 0.1f, 0f]), new([0f, 1f, 0f]), new([0f, 0f, 1f])],
			Documents = ["The quick brown fox jumps over the lazy dog", "A fast auburn fox leaps over a sleepy canine",
				"Machine learning is a subset of artificial intelligence", "Grandma bakes an apple pie every Sunday"],
			Metadatas =
			[
				new Dictionary<string, object> { ["topic"] = "animals" },
				new Dictionary<string, object> { ["topic"] = "animals" },
				new Dictionary<string, object> { ["topic"] = "tech" },
				new Dictionary<string, object> { ["topic"] = "food" },
			],
		});
		var fillers = Enumerable.Range(0, 300).Select(i => $"f{i}").ToList();
		await collection.AddAsync(new ChromaRecords(fillers)
		{
			Embeddings = fillers.Select((_, i) => new ReadOnlyMemory<float>([0.5f, 0.5f, 0.3f + i / 1000f])).ToList(),
			Documents = fillers.Select((_, i) => $"filler record number {i} about weather and trains").ToList(),
			Metadatas = fillers.Select(_ => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["topic"] = "tech" }).ToList(),
		});
		return collection;
	}

	[Test]
	public async Task SparseVectors()
	{
		var collection = await SparseCollectionAsync("sparse_knn");
		var rows = await collection.SearchAsync(new ChromaSearch
		{
			Rank = ChromaRank.SparseKnn(new ChromaSparseVector([1], [1.0f]), "sparse"),
			Limit = 4,
			Select = [ChromaSearchKeys.Score],
		});
		AssertRows(rows, [("s1", 0.0), ("s3", 0.7)], 1e-6);
	}

	[Test]
	public async Task RrfOfDenseAndSparseWithWhere()
	{
		var collection = await SparseCollectionAsync("sparse_rrf");
		var rows = await collection.SearchAsync(new ChromaSearch
		{
			Where = ChromaWhereOperator.GreaterThanOrEqual("k", 1),
			Rank = ChromaRank.Rrf([ChromaRank.Knn(new float[] { 1, 0 }, returnRank: true), ChromaRank.SparseKnn(new ChromaSparseVector([1], [1.0f]), "sparse", returnRank: true)], 60, [0.7, 0.3], false),
			Limit = 4,
			Select = [ChromaSearchKeys.Score],
		});
		AssertRows(rows, [("s1", -0.016666668), ("s3", -0.016393442)], 1e-9);
	}

	// The vector of the query is computed by ChromaDotNet.Client, those of the documents by the server.
	[Test]
	public async Task Bm25()
	{
		var collection = await Bm25CollectionAsync("bm25_knn");
		var rows = await collection.SearchAsync(new ChromaSearch { Rank = ChromaRank.SparseKnn("fox", "doc_bm25"), Limit = 4, Select = [ChromaSearchKeys.Score] });
		AssertRows(rows, [("fox1", -12.50207), ("fox2", -12.50207)], 5e-6);
	}

	[Test]
	public async Task RrfOfDenseAndBm25WithWhere()
	{
		var collection = await Bm25CollectionAsync("bm25_rrf");
		var rows = await collection.SearchAsync(new ChromaSearch
		{
			Where = ChromaWhereOperator.NotEqual("topic", "tech"),
			Rank = ChromaRank.Rrf([ChromaRank.Knn(new float[] { 1, 0, 0 }, returnRank: true), ChromaRank.SparseKnn("apple pie", "doc_bm25", returnRank: true)], 60, [0.7, 0.3], false),
			Limit = 4,
			Select = [ChromaSearchKeys.Score],
		});
		AssertRows(rows, [("pie", -0.016290322)], 1e-9);
	}

	// No rank: the records in the order of the storage, after the where and the offset.
	[Test]
	public async Task NoRankWithWhereAndOffset()
	{
		var collection = await Bm25CollectionAsync("bm25_no_rank");
		var rows = await collection.SearchAsync(new ChromaSearch { Where = ChromaWhereOperator.NotEqual("topic", "tech"), Limit = 3, Offset = 1, Select = [ChromaSearchKeys.Score] });
		AssertRows(rows, [("fox2", 0), ("pie", 1)], 0);
	}

	[Test]
	public async Task GroupByAndSelect()
	{
		var groups = _client.GetCollectionClient(await _client.CreateCollectionAsync(new ChromaCollectionDefinition("group_by")));
		string[] ids = ["sci_2023_1", "sci_2023_2", "sci_2024_1", "sci_2024_2", "tech_2023_1", "tech_2023_2", "tech_2024_1", "tech_2024_2", "arts_2023_1", "arts_2023_2", "arts_2024_1", "arts_2024_2"];
		float[][] embeddings = [[1.0f, 0, 0, 0], [0.9f, 0.1f, 0, 0], [0.8f, 0.2f, 0, 0], [0.7f, 0.3f, 0, 0], [0, 1.0f, 0, 0], [0, 0.9f, 0.1f, 0], [0, 0.8f, 0.2f, 0], [0, 0.7f, 0.3f, 0],
			[0, 0, 1.0f, 0], [0, 0, 0.9f, 0.1f], [0, 0, 0.8f, 0.2f], [0, 0, 0.7f, 0.3f]];
		(string Category, int Year, int Priority)[] metadata = [("science", 2023, 1), ("science", 2023, 2), ("science", 2024, 1), ("science", 2024, 3), ("tech", 2023, 2), ("tech", 2023, 1), ("tech", 2024, 1),
			("tech", 2024, 2), ("arts", 2023, 3), ("arts", 2023, 1), ("arts", 2024, 2), ("arts", 2024, 1)];
		await groups.AddAsync(new ChromaRecords(ids)
		{
			Embeddings = embeddings.Select(e => new ReadOnlyMemory<float>(e)).ToList(),
			Metadatas = metadata.Select(m => (IReadOnlyDictionary<string, object>)new Dictionary<string, object> { ["category"] = m.Category, ["year"] = m.Year, ["priority"] = m.Priority }).ToList(),
			Documents = ids.Select(id => $"doc_{id}").ToList(),
		});
		var query = ChromaRank.Knn(new float[] { 1, 0, 0, 0 }, limit: 12);
		foreach (var (name, groupBy, expected) in new (string, ChromaSearchGroupBy, string[])[]
		{
			("category, the 2 of lowest score", new(ChromaSearchAggregate.MinK(2, ChromaSearchKeys.Score), "category"),
				["sci_2023_1", "sci_2023_2", "tech_2024_2", "tech_2024_1", "arts_2024_2", "arts_2024_1"]),
			("category and year, the 1 of lowest score", new(ChromaSearchAggregate.MinK(1, ChromaSearchKeys.Score), "category", "year"),
				["sci_2023_1", "sci_2024_1", "tech_2023_2", "tech_2024_2", "arts_2023_2", "arts_2024_2"]),
			("category, the 2 of lowest priority, then score", new(ChromaSearchAggregate.MinK(2, "priority", ChromaSearchKeys.Score), "category"),
				["sci_2023_1", "sci_2024_1", "tech_2024_1", "tech_2023_2", "arts_2024_2", "arts_2023_2"]),
		})
		{
			var grouped = await groups.SearchAsync(new ChromaSearch { Rank = query, GroupBy = groupBy, Limit = 12 });
			Assert.That(grouped.Select(r => r.Id), Is.EquivalentTo(expected), name);
		}

		// The document and one field of the metadata, without the score.
		var rows = await groups.SearchAsync(new ChromaSearch { Rank = ChromaRank.Knn(new float[] { 1, 0, 0, 0 }), Limit = 1, Select = [ChromaSearchKeys.Document, "category"] });
		Assert.That(rows, Has.Count.EqualTo(1));
		Assert.That(rows[0].Id, Is.EqualTo("sci_2023_1"));
		Assert.That(rows[0].Document, Is.EqualTo("doc_sci_2023_1"));
		Assert.That(rows[0].Score, Is.Null);
		Assert.That(rows[0].Metadata, Is.EquivalentTo(new Dictionary<string, object> { ["category"] = "science" }));
	}

	private static void AssertRows(IReadOnlyList<ChromaSearchEntry> rows, (string Id, double Score)[] expected, double tolerance)
	{
		var shown = string.Join(", ", rows.Select(r => $"{r.Id} {r.Score}"));
		Assert.That(rows.Select(r => r.Id), Is.EqualTo(expected.Select(e => e.Id)), shown);
		for (var i = 0; i < expected.Length; i++)
			Assert.That(rows[i].Score!.Value, Is.EqualTo(expected[i].Score).Within(tolerance), shown);
	}
}
