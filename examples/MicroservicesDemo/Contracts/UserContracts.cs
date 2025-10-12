using MemoryPack;

namespace MicroservicesDemo.Contracts;

[MemoryPackable]
public partial class GetUserRequest
{
    public int UserId { get; set; }
}

[MemoryPackable]
public partial class GetUserResponse
{
    public int UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; }
}

[MemoryPackable]
public partial class ValidateUserRequest
{
    public int UserId { get; set; }
}

[MemoryPackable]
public partial class ValidateUserResponse
{
    public bool IsValid { get; set; }
    public string? Message { get; set; }
}

