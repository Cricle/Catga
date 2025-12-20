using Catga.Core;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Comprehensive tests for IdGenerator
/// </summary>
public class IdGeneratorComprehensiveTests
{
    [Fact]
    public void NewBase64Id_ShouldReturnValidBase64String()
    {
        var id = IdGenerator.NewBase64Id();
        
        id.Should().NotBeNullOrEmpty();
        // Base64 of 16 bytes = 24 characters (with padding)
        id.Length.Should().Be(24);
        
        // Should be valid base64
        var act = () => Convert.FromBase64String(id);
        act.Should().NotThrow();
    }

    [Fact]
    public void NewBase64Id_ShouldReturnUniqueIds()
    {
        var ids = new HashSet<string>();
        
        for (int i = 0; i < 1000; i++)
        {
            var id = IdGenerator.NewBase64Id();
            ids.Add(id).Should().BeTrue($"Duplicate ID found at iteration {i}");
        }
    }

    [Fact]
    public void NewBase64Id_ShouldDecodeToGuid()
    {
        var id = IdGenerator.NewBase64Id();
        var bytes = Convert.FromBase64String(id);
        
        bytes.Length.Should().Be(16);
        
        // Should be able to create a Guid from the bytes
        var act = () => new Guid(bytes);
        act.Should().NotThrow();
    }

    [Fact]
    public void NewBase64IdNoPadding_ShouldReturnValidBase64WithoutPadding()
    {
        var id = IdGenerator.NewBase64IdNoPadding();
        
        id.Should().NotBeNullOrEmpty();
        // Base64 of 16 bytes without padding = 22 characters
        id.Length.Should().Be(22);
        id.Should().NotEndWith("=");
    }

    [Fact]
    public void NewBase64IdNoPadding_ShouldReturnUniqueIds()
    {
        var ids = new HashSet<string>();
        
        for (int i = 0; i < 1000; i++)
        {
            var id = IdGenerator.NewBase64IdNoPadding();
            ids.Add(id).Should().BeTrue($"Duplicate ID found at iteration {i}");
        }
    }

    [Fact]
    public void NewBase64IdNoPadding_ShouldBeDecodableWithPadding()
    {
        var id = IdGenerator.NewBase64IdNoPadding();
        
        // Add padding back to decode
        var paddedId = id + "==";
        var bytes = Convert.FromBase64String(paddedId);
        
        bytes.Length.Should().Be(16);
    }

    [Fact]
    public void NewBase64Id_ShouldBeThreadSafe()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = new List<Task>();
        
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    ids.Add(IdGenerator.NewBase64Id());
                }
            }));
        }
        
        Task.WaitAll(tasks.ToArray());
        
        ids.Count.Should().Be(1000);
        ids.Distinct().Count().Should().Be(1000);
    }

    [Fact]
    public void NewBase64IdNoPadding_ShouldBeThreadSafe()
    {
        var ids = new System.Collections.Concurrent.ConcurrentBag<string>();
        var tasks = new List<Task>();
        
        for (int i = 0; i < 10; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 100; j++)
                {
                    ids.Add(IdGenerator.NewBase64IdNoPadding());
                }
            }));
        }
        
        Task.WaitAll(tasks.ToArray());
        
        ids.Count.Should().Be(1000);
        ids.Distinct().Count().Should().Be(1000);
    }

    [Fact]
    public void NewBase64Id_ShouldContainOnlyValidBase64Characters()
    {
        var id = IdGenerator.NewBase64Id();
        
        // Valid base64 characters: A-Z, a-z, 0-9, +, /, =
        id.Should().MatchRegex(@"^[A-Za-z0-9+/=]+$");
    }

    [Fact]
    public void NewBase64IdNoPadding_ShouldContainOnlyValidBase64Characters()
    {
        var id = IdGenerator.NewBase64IdNoPadding();
        
        // Valid base64 characters without padding: A-Z, a-z, 0-9, +, /
        id.Should().MatchRegex(@"^[A-Za-z0-9+/]+$");
    }
}
