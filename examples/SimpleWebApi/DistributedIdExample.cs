using Catga.DistributedId;
using Microsoft.AspNetCore.Mvc;

namespace SimpleWebApi;

/// <summary>
/// Distributed ID examples
/// </summary>
public static class DistributedIdExample
{
    public static void MapDistributedIdEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/distributed-id")
            .WithTags("Distributed ID")
            .WithOpenApi();

        // Generate single ID
        group.MapGet("/generate", (IDistributedIdGenerator idGen) =>
        {
            var id = idGen.NextId();
            var metadata = idGen.ParseId(id);

            return Results.Ok(new
            {
                id,
                idString = id.ToString(),
                metadata = new
                {
                    metadata.WorkerId,
                    metadata.Sequence,
                    metadata.Timestamp,
                    GeneratedAt = metadata.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss.fff")
                }
            });
        })
        .WithName("GenerateId")
        .WithSummary("Generate a distributed ID");

        // Generate batch IDs
        group.MapGet("/generate/batch/{count:int}", (
            [FromRoute] int count,
            IDistributedIdGenerator idGen) =>
        {
            if (count <= 0 || count > 1000)
            {
                return Results.BadRequest("Count must be between 1 and 1000");
            }

            var ids = new List<long>();
            for (int i = 0; i < count; i++)
            {
                ids.Add(idGen.NextId());
            }

            return Results.Ok(new
            {
                count = ids.Count,
                ids,
                firstId = ids[0],
                lastId = ids[^1],
                allUnique = ids.Count == ids.Distinct().Count()
            });
        })
        .WithName("GenerateBatchIds")
        .WithSummary("Generate multiple distributed IDs");

        // Parse ID
        group.MapGet("/parse/{id:long}", (
            [FromRoute] long id,
            IDistributedIdGenerator idGen) =>
        {
            var metadata = idGen.ParseId(id);

            return Results.Ok(new
            {
                id,
                metadata = new
                {
                    metadata.WorkerId,
                    metadata.Sequence,
                    metadata.Timestamp,
                    GeneratedAt = metadata.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss.fff UTC"),
                    ElapsedSinceGeneration = DateTime.UtcNow - metadata.GeneratedAt
                }
            });
        })
        .WithName("ParseId")
        .WithSummary("Parse distributed ID to extract metadata");

        // Performance test
        group.MapPost("/performance", (
            [FromBody] PerformanceTestRequest request,
            IDistributedIdGenerator idGen) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ids = new HashSet<long>();

            for (int i = 0; i < request.Count; i++)
            {
                ids.Add(idGen.NextId());
            }

            sw.Stop();

            return Results.Ok(new
            {
                generated = request.Count,
                unique = ids.Count,
                elapsedMs = sw.ElapsedMilliseconds,
                idsPerSecond = (long)(request.Count / sw.Elapsed.TotalSeconds),
                allUnique = ids.Count == request.Count
            });
        })
        .WithName("PerformanceTest")
        .WithSummary("Test ID generation performance");

        // Concurrent generation test
        group.MapPost("/performance/concurrent", async (
            [FromBody] ConcurrentTestRequest request,
            IDistributedIdGenerator idGen) =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ids = new System.Collections.Concurrent.ConcurrentBag<long>();

            var tasks = Enumerable.Range(0, request.Threads)
                .Select(_ => Task.Run(() =>
                {
                    for (int i = 0; i < request.IdsPerThread; i++)
                    {
                        ids.Add(idGen.NextId());
                    }
                }))
                .ToArray();

            await Task.WhenAll(tasks);
            sw.Stop();

            var uniqueIds = new HashSet<long>(ids);

            return Results.Ok(new
            {
                threads = request.Threads,
                idsPerThread = request.IdsPerThread,
                totalGenerated = ids.Count,
                uniqueIds = uniqueIds.Count,
                elapsedMs = sw.ElapsedMilliseconds,
                idsPerSecond = (long)(ids.Count / sw.Elapsed.TotalSeconds),
                allUnique = uniqueIds.Count == ids.Count,
                duplicates = ids.Count - uniqueIds.Count
            });
        })
        .WithName("ConcurrentPerformanceTest")
        .WithSummary("Test concurrent ID generation performance");
    }

    public record PerformanceTestRequest
    {
        public int Count { get; init; } = 10000;
    }

    public record ConcurrentTestRequest
    {
        public int Threads { get; init; } = 10;
        public int IdsPerThread { get; init; } = 1000;
    }
}

