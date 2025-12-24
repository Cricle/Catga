using Catga.Hosting;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// 托管服务配置选项单元测试
/// </summary>
public class HostingOptionsTests
{
    [Fact]
    public void HostingOptions_DefaultValues_AreValid()
    {
        // Arrange & Act
        var options = new HostingOptions();
        
        // Assert
        Assert.True(options.EnableAutoRecovery);
        Assert.True(options.EnableTransportHosting);
        Assert.True(options.EnableOutboxProcessor);
        Assert.Equal(TimeSpan.FromSeconds(30), options.ShutdownTimeout);
        Assert.NotNull(options.Recovery);
        Assert.NotNull(options.OutboxProcessor);
    }
    
    [Fact]
    public void HostingOptions_Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new HostingOptions
        {
            ShutdownTimeout = TimeSpan.FromSeconds(60)
        };
        
        // Act & Assert
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }
    
    [Fact]
    public void HostingOptions_Validate_WithZeroShutdownTimeout_ThrowsArgumentException()
    {
        // Arrange
        var options = new HostingOptions
        {
            ShutdownTimeout = TimeSpan.Zero
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("ShutdownTimeout", exception.Message);
    }
    
    [Fact]
    public void HostingOptions_Validate_WithNegativeShutdownTimeout_ThrowsArgumentException()
    {
        // Arrange
        var options = new HostingOptions
        {
            ShutdownTimeout = TimeSpan.FromSeconds(-1)
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("ShutdownTimeout", exception.Message);
    }
}
