using Xunit;

namespace Catga.Tests;

[CollectionDefinition("NatsTransport", DisableParallelization = true)]
public sealed class NatsTransportCollectionDefinition
{
}

[CollectionDefinition("RabbitMqTransport", DisableParallelization = true)]
public sealed class RabbitMqTransportCollectionDefinition
{
}

[CollectionDefinition("E2E Tests", DisableParallelization = true)]
public sealed class E2ETestsCollectionDefinition
{
}
