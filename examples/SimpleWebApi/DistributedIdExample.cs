using Catga.DistributedId;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;

namespace SimpleWebApi;

/// <summary>
/// Distributed ID examples
/// </summary>
public static class DistributedIdExample
{
    [UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
        Justification = "OpenAPI generation is optional and not required for production AOT scenarios")]
    [UnconditionalSuppressMessage("AOT", "IL3050:RequiresDynamicCode",
        Justification = "OpenAPI generation is optional and not required for production AOT scenarios")]
    public static void MapDistributedIdEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/distributed-id")
            .WithTags("Distributed ID")
            .WithOpenApi();

        // Generate single ID (0 GC demonstration)
        group.MapGet("/generate", (IDistributedIdGenerator idGen) =>
        {
            var id = idGen.NextId();  // 0 bytes allocated (lock-free)
            idGen.ParseId(id, out var metadata);  // 0 bytes allocated (zero-allocation version)

            return Results.Ok(new
            {
                id,
                idString = id.ToString(),
                features = "Lock-free + 0 GC + Custom Epoch",
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
        .WithSummary("Generate a distributed ID (lock-free, 0 allocation)");

        // Generate batch IDs (optimized with NextIds)
        group.MapGet("/generate/batch/{count:int}", (
            [FromRoute] int count,
            IDistributedIdGenerator idGen) =>
        {
            if (count <= 0 || count > 10000)
            {
                return Results.BadRequest("Count must be between 1 and 10000");
            }

            // Use optimized batch generation (10-100x faster than loop)
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ids = idGen.NextIds(count);  // Lock-free batch reservation
            sw.Stop();

            return Results.Ok(new
            {
                count = ids.Length,
                ids,
                firstId = ids[0],
                lastId = ids[^1],
                allUnique = ids.Length == ids.Distinct().Count(),
                performance = new
                {
                    elapsedMicroseconds = sw.Elapsed.TotalMicroseconds,
                    idsPerSecond = (long)(count / sw.Elapsed.TotalSeconds),
                    method = "Batch (lock-free CAS reservation)"
                }
            });
        })
        .WithName("GenerateBatchIds")
        .WithSummary("Generate multiple distributed IDs (optimized batch mode)");

        // Parse ID (0 GC demonstration)
        group.MapGet("/parse/{id:long}", (
            [FromRoute] long id,
            IDistributedIdGenerator idGen) =>
        {
            idGen.ParseId(id, out var metadata);  // 0 bytes allocated

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
        .WithSummary("Parse distributed ID to extract metadata (0 allocation)");

        // Get layout information (shows custom epoch)
        group.MapGet("/layout", (IDistributedIdGenerator idGen) =>
        {
            var generator = idGen as SnowflakeIdGenerator;
            if (generator == null)
            {
                return Results.BadRequest("Not a SnowflakeIdGenerator");
            }

            var layout = generator.GetLayout();
            return Results.Ok(new
            {
                description = layout.ToString(),
                configuration = new
                {
                    timestampBits = layout.TimestampBits,
                    workerIdBits = layout.WorkerIdBits,
                    sequenceBits = layout.SequenceBits
                },
                capacity = new
                {
                    maxYears = layout.MaxYears,
                    maxWorkers = layout.MaxWorkerId + 1,
                    maxIdsPerMillisecond = layout.SequenceMask + 1
                },
                epoch = new
                {
                    epochUtc = layout.GetEpoch().ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    epochMilliseconds = layout.EpochMilliseconds
                }
            });
        })
        .WithName("GetIdLayout")
        .WithSummary("Get bit layout configuration and custom epoch information");

        // Performance test (comparison: loop vs batch)
        group.MapPost("/performance", (
            [FromBody] PerformanceTestRequest request,
            IDistributedIdGenerator idGen) =>
        {
            // Test 1: Individual calls (loop)
            var sw1 = System.Diagnostics.Stopwatch.StartNew();
            var ids1 = new HashSet<long>();
            for (int i = 0; i < request.Count; i++)
            {
                ids1.Add(idGen.NextId());
            }
            sw1.Stop();

            // Test 2: Batch generation
            var sw2 = System.Diagnostics.Stopwatch.StartNew();
            var ids2 = idGen.NextIds(request.Count);
            sw2.Stop();

            return Results.Ok(new
            {
                count = request.Count,
                loopMethod = new
                {
                    elapsedMs = sw1.ElapsedMilliseconds,
                    idsPerSecond = (long)(request.Count / sw1.Elapsed.TotalSeconds),
                    unique = ids1.Count
                },
                batchMethod = new
                {
                    elapsedMs = sw2.ElapsedMilliseconds,
                    idsPerSecond = (long)(request.Count / sw2.Elapsed.TotalSeconds),
                    unique = ids2.Distinct().Count()
                },
                speedup = $"{sw1.Elapsed.TotalMilliseconds / sw2.Elapsed.TotalMilliseconds:F2}x faster"
            });
        })
        .WithName("PerformanceTest")
        .WithSummary("Test ID generation performance (loop vs batch)");

        // Concurrent generation test (demonstrates lock-free performance)
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
                        ids.Add(idGen.NextId());  // Lock-free, 0 GC
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
                duplicates = ids.Count - uniqueIds.Count,
                performance = new
                {
                    algorithm = "SpinLock (lock-free)",
                    allocation = "0 bytes per call",
                    concurrency = "Thread-safe"
                }
            });
        })
        .WithName("ConcurrentPerformanceTest")
        .WithSummary("Test concurrent ID generation performance (lock-free algorithm)");
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

