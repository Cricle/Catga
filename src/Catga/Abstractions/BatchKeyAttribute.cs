namespace Catga.Abstractions;

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class BatchKeyAttribute : Attribute
{
    public string PropertyName { get; }
    public BatchKeyAttribute(string propertyName)
    {
        PropertyName = propertyName;
    }
}
