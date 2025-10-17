// ReSharper disable once CheckNamespace
#if !NET7_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Polyfill for RequiresDynamicCode attribute in .NET 6 and earlier
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class, Inherited = false)]
[ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never)]
internal sealed class RequiresDynamicCodeAttribute : Attribute
{
    public RequiresDynamicCodeAttribute(string message)
    {
        Message = message;
    }

    public string Message { get; }
    public string? Url { get; set; }
}
#endif

