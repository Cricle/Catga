using Catga.Hosting;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// Outbox 处理器配置选项单元测试
/// </summary>
public class OutboxProcessorOptionsTests
{
    [Fact]
    public void OutboxProcessorOptions_DefaultValues_AreValid()
    {
        // Arrange & Act
        var options = new OutboxProcessorOptions();
        
        // Assert
        Assert.Equal(TimeSpan.FromSeconds(5), options.ScanInterval);
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(TimeSpan.FromSeconds(10), options.ErrorDelay);
        Assert.True(options.CompleteCurrentBatchOnShutdown);
    }
    
    [Fact]
    public void OutboxProcessorOptions_Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.FromSeconds(10),
            BatchSize = 50,
            ErrorDelay = TimeSpan.FromSeconds(5)
        };
        
        // Act & Assert
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }
    
    [Fact]
    public void OutboxProcessorOptions_Validate_WithZeroScanInterval_ThrowsArgumentException()
    {
        // Arrange
        var options = new OutboxProcessorOptions
        {
            ScanInterval = TimeSpan.Zero
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("ScanInterval", exception.Message);
    }
    
    [Fact]
    public void OutboxProcessorOptions_Validate_WithZeroBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new OutboxProcessorOptions
        {
            BatchSize = 0
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("BatchSize", exception.Message);
    }
    
    [Fact]
    public void OutboxProcessorOptions_Validate_WithNegativeBatchSize_ThrowsArgumentException()
    {
        // Arrange
        var options = new OutboxProcessorOptions
        {
            BatchSize = -1
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("BatchSize", exception.Message);
    }
    
    [Fact]
    public void OutboxProcessorOptions_Validate_WithNegativeErrorDelay_ThrowsArgumentException()
    {
        // Arrange
        var options = new OutboxProcessorOptions
        {
            ErrorDelay = TimeSpan.FromSeconds(-1)
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("ErrorDelay", exception.Message);
    }
}
