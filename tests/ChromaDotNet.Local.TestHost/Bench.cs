using System.Diagnostics;
using ChromaDB.Client;
using ChromaDotNet.Local;

internal static class Bench
{
	public static async Task<int> RunAsync(string directory)
	{
		Directory.CreateDirectory(directory);
		var starts = new List<long>();
		var stops = new List<long>();
		for (var i = 0; i < 5; i++)
		{
			var stopwatch = Stopwatch.StartNew();
			var chroma = await ChromaLocalServer.StartAsync(new ChromaLocalOptions { Engine = HostEngine.Chosen, DataPath = Path.Combine(directory, "start" + i) });
			starts.Add(stopwatch.ElapsedMilliseconds);
			stopwatch.Restart();
			await chroma.DisposeAsync();
			stops.Add(stopwatch.ElapsedMilliseconds);
		}
		Console.WriteLine($"start ms: {string.Join(", ", starts)}");
		Console.WriteLine($"stop ms: {string.Join(", ", stops)}");

		// 10,000 records of 384 dimensions, a stop, and a start on the same data.
		var path = Path.Combine(directory, "restart");
		const int Count = 10_000;
		await using (var chroma = await ChromaLocalServer.StartAsync(new ChromaLocalOptions { Engine = HostEngine.Chosen, DataPath = path }))
		{
			using var client = chroma.CreateClient();
			var records = client.GetCollectionClient(await client.GetOrCreateCollectionAsync("bench"));
			var random = new Random(42);
			var stopwatch = Stopwatch.StartNew();
			for (var batch = 0; batch < Count / 1000; batch++)
			{
				var ids = Enumerable.Range(batch * 1000, 1000).Select(n => "id" + n).ToList();
				var embeddings = ids.Select(_ => new ReadOnlyMemory<float>(Enumerable.Range(0, 384).Select(_ => (float)random.NextDouble()).ToArray())).ToList();
				await records.AddAsync(ids, embeddings);
			}
			Console.WriteLine($"add {Count} records ms: {stopwatch.ElapsedMilliseconds}");
			stopwatch.Restart();
			await chroma.StopAsync();
			Console.WriteLine($"stop with {Count} records ms: {stopwatch.ElapsedMilliseconds}");
		}
		{
			var stopwatch = Stopwatch.StartNew();
			await using var chroma = await ChromaLocalServer.StartAsync(new ChromaLocalOptions { Engine = HostEngine.Chosen, DataPath = path });
			var started = stopwatch.ElapsedMilliseconds;
			using var client = chroma.CreateClient();
			var records = client.GetCollectionClient(await client.GetCollectionAsync("bench"));
			var count = await records.CountAsync();
			var query = await records.QueryAsync(new ReadOnlyMemory<float>(Enumerable.Range(0, 384).Select(_ => 0.5f).ToArray()), nResults: 5);
			Console.WriteLine($"restart ms: {started}, count {count}, first query ms: {stopwatch.ElapsedMilliseconds - started}, results {query.Count}");
		}

		{
			var stopwatch = Stopwatch.StartNew();
			var both = await Task.WhenAll(
				ChromaLocalServer.StartAsync(new ChromaLocalOptions { Engine = HostEngine.Chosen, DataPath = Path.Combine(directory, "a") }),
				ChromaLocalServer.StartAsync(new ChromaLocalOptions { Engine = HostEngine.Chosen, DataPath = Path.Combine(directory, "b") }));
			Console.WriteLine($"two at the same time ms: {stopwatch.ElapsedMilliseconds}, ports {both[0].Port} and {both[1].Port}");
			foreach (var chroma in both)
				await chroma.DisposeAsync();
		}
		return 0;
	}
}
