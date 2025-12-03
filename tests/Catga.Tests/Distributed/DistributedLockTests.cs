using Catga.Abstractions;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Distributed;

public class DistributedLockTests
{
    [Fact]
    public void LockAcquisitionException_ContainsResourceAndTimeout()
    {
        var resource = "test-resource";
        var timeout = TimeSpan.FromSeconds(5);

        var ex = new LockAcquisitionException(resource, timeout);

        ex.Resource.Should().Be(resource);
        ex.WaitTimeout.Should().Be(timeout);
        ex.Message.Should().Contain(resource);
        ex.Message.Should().Contain("5");
    }

    [Fact]
    public void LockLostException_ContainsResourceAndLockId()
    {
        var resource = "test-resource";
        var lockId = "lock-123";

        var ex = new LockLostException(resource, lockId);

        ex.Resource.Should().Be(resource);
        ex.LockId.Should().Be(lockId);
        ex.Message.Should().Contain(resource);
        ex.Message.Should().Contain(lockId);
    }

    [Fact]
    public void DistributedLockOptions_HasSensibleDefaults()
    {
        var options = new DistributedLockOptions();

        options.DefaultExpiry.Should().Be(TimeSpan.FromSeconds(30));
        options.DefaultWaitTimeout.Should().Be(TimeSpan.FromSeconds(10));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(50));
        options.EnableAutoExtend.Should().BeFalse();
        options.AutoExtendInterval.Should().Be(TimeSpan.FromSeconds(10));
        options.KeyPrefix.Should().Be("catga:lock:");
    }

    [Fact]
    public void DistributedLockOptions_CanBeCustomized()
    {
        var options = new DistributedLockOptions
        {
            DefaultExpiry = TimeSpan.FromMinutes(1),
            DefaultWaitTimeout = TimeSpan.FromSeconds(30),
            RetryInterval = TimeSpan.FromMilliseconds(100),
            EnableAutoExtend = true,
            AutoExtendInterval = TimeSpan.FromSeconds(20),
            KeyPrefix = "myapp:lock:"
        };

        options.DefaultExpiry.Should().Be(TimeSpan.FromMinutes(1));
        options.DefaultWaitTimeout.Should().Be(TimeSpan.FromSeconds(30));
        options.RetryInterval.Should().Be(TimeSpan.FromMilliseconds(100));
        options.EnableAutoExtend.Should().BeTrue();
        options.AutoExtendInterval.Should().Be(TimeSpan.FromSeconds(20));
        options.KeyPrefix.Should().Be("myapp:lock:");
    }
}
