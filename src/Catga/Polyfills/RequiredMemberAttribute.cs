// ReSharper disable once CheckNamespace
#if !NET7_0_OR_GREATER
namespace System.Runtime.CompilerServices;

/// <summary>
/// Polyfill for required members in .NET 6 and earlier
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
[ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never)]
internal sealed class RequiredMemberAttribute : Attribute
{
}

/// <summary>
/// Polyfill for compiler feature required attribute
/// </summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
[ComponentModel.EditorBrowsable(ComponentModel.EditorBrowsableState.Never)]
internal sealed class CompilerFeatureRequiredAttribute : Attribute
{
    public CompilerFeatureRequiredAttribute(string featureName)
    {
        FeatureName = featureName;
    }

    public string FeatureName { get; }
    public bool IsOptional { get; init; }
}
#endif

