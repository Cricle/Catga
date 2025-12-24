using Catga.Hosting;
using Xunit;

namespace Catga.Tests.Hosting;

/// <summary>
/// 恢复服务配置选项单元测试
/// </summary>
public class RecoveryOptionsTests
{
    [Fact]
    public void RecoveryOptions_DefaultValues_AreValid()
    {
        // Arrange & Act
        var options = new RecoveryOptions();
        
        // Assert
        Assert.Equal(TimeSpan.FromSeconds(30), options.CheckInterval);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(5), options.RetryDelay);
        Assert.True(options.EnableAutoRecovery);
        Assert.True(options.UseExponentialBackoff);
    }
    
    [Fact]
    public void RecoveryOptions_Validate_WithValidOptions_DoesNotThrow()
    {
        // Arrange
        var options = new RecoveryOptions
        {
            CheckInterval = TimeSpan.FromSeconds(60),
            MaxRetries = 5,
            RetryDelay = TimeSpan.FromSeconds(10)
        };
        
        // Act & Assert
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }
    
    [Fact]
    public void RecoveryOptions_Validate_WithZeroCheckInterval_ThrowsArgumentException()
    {
        // Arrange
        var options = new RecoveryOptions
        {
            CheckInterval = TimeSpan.Zero
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("CheckInterval", exception.Message);
    }
    
    [Fact]
    public void RecoveryOptions_Validate_WithNegativeMaxRetries_ThrowsArgumentException()
    {
        // Arrange
        var options = new RecoveryOptions
        {
            MaxRetries = -1
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("MaxRetries", exception.Message);
    }
    
    [Fact]
    public void RecoveryOptions_Validate_WithNegativeRetryDelay_ThrowsArgumentException()
    {
        // Arrange
        var options = new RecoveryOptions
        {
            RetryDelay = TimeSpan.FromSeconds(-1)
        };
        
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() => options.Validate());
        Assert.Contains("RetryDelay", exception.Message);
    }
    
    [Fact]
    public void RecoveryOptions_Validate_WithZeroMaxRetries_IsValid()
    {
        // Arrange
        var options = new RecoveryOptions
        {
            MaxRetries = 0
        };
        
        // Act & Assert
        var exception = Record.Exception(() => options.Validate());
        Assert.Null(exception);
    }
}
