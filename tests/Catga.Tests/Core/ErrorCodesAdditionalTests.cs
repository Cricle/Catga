using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

public class ErrorCodesAdditionalTests
{
    [Fact]
    public void ErrorCodes_InternalError_IsNotNullOrEmpty()
    {
        ErrorCodes.InternalError.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_ValidationFailed_IsNotNullOrEmpty()
    {
        ErrorCodes.ValidationFailed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_Timeout_IsNotNullOrEmpty()
    {
        ErrorCodes.Timeout.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_Cancelled_IsNotNullOrEmpty()
    {
        ErrorCodes.Cancelled.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_HandlerFailed_IsNotNullOrEmpty()
    {
        ErrorCodes.HandlerFailed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_PersistenceFailed_IsNotNullOrEmpty()
    {
        ErrorCodes.PersistenceFailed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_TransportFailed_IsNotNullOrEmpty()
    {
        ErrorCodes.TransportFailed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_SerializationFailed_IsNotNullOrEmpty()
    {
        ErrorCodes.SerializationFailed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_LockFailed_IsNotNullOrEmpty()
    {
        ErrorCodes.LockFailed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_PipelineFailed_IsNotNullOrEmpty()
    {
        ErrorCodes.PipelineFailed.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ErrorCodes_AllCodesAreUnique()
    {
        var codes = new[]
        {
            ErrorCodes.InternalError,
            ErrorCodes.ValidationFailed,
            ErrorCodes.Timeout,
            ErrorCodes.Cancelled,
            ErrorCodes.HandlerFailed,
            ErrorCodes.PersistenceFailed,
            ErrorCodes.TransportFailed,
            ErrorCodes.SerializationFailed,
            ErrorCodes.LockFailed,
            ErrorCodes.PipelineFailed
        };

        codes.Distinct().Count().Should().Be(codes.Length);
    }
}
