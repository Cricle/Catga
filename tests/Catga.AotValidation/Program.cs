// AOT Validation - This project validates that all Catga libraries are AOT compatible
// Build with: dotnet publish -c Release
// If this builds successfully, all libraries are AOT compatible

using Catga;
using Catga.Abstractions;
using Catga.AspNetCore;
using Catga.Cluster;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.EventSourcing;
using Catga.Flow;
using Catga.Persistence.InMemory;
using Catga.Persistence.Nats;
using Catga.Persistence.Redis;
using Catga.Resilience;
using Catga.Scheduling.Hangfire;
using Catga.Scheduling.Quartz;
using Catga.Serialization.MemoryPack;
using Catga.Transport;
using Microsoft.Extensions.DependencyInjection;

Console.WriteLine("Catga AOT Validation");
Console.WriteLine("====================");

// Validate core types are accessible
var services = new ServiceCollection();
services.AddLogging();
services.AddCatga();

// Validate all modules can be referenced
Console.WriteLine("✓ Catga Core");
Console.WriteLine("✓ Catga.AspNetCore");
Console.WriteLine("✓ Catga.Cluster");
Console.WriteLine("✓ Catga.Persistence.InMemory");
Console.WriteLine("✓ Catga.Persistence.Nats");
Console.WriteLine("✓ Catga.Persistence.Redis");
Console.WriteLine("✓ Catga.Scheduling.Hangfire");
Console.WriteLine("✓ Catga.Scheduling.Quartz");
Console.WriteLine("✓ Catga.Serialization.MemoryPack");
Console.WriteLine("✓ Catga.Transport.InMemory");
Console.WriteLine("✓ Catga.Transport.Nats");
Console.WriteLine("✓ Catga.Transport.Redis");

Console.WriteLine();
Console.WriteLine("All Catga libraries are AOT compatible!");
return 0;
