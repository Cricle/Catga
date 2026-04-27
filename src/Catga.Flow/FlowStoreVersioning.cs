namespace Catga.Flow.Dsl;

/// <summary>
/// Declares how an <see cref="IDslFlowStore"/> interprets snapshot versions on update.
/// </summary>
public interface IDslFlowStoreVersioning
{
    DslFlowStoreVersioningMode VersioningMode { get; }
}

/// <summary>
/// Versioning modes supported by DSL flow stores.
/// </summary>
public enum DslFlowStoreVersioningMode
{
    /// <summary>
    /// Caller submits the next version to persist and the store writes it as-is.
    /// </summary>
    CallerSuppliesNextVersion = 0,

    /// <summary>
    /// Caller submits the current persisted version and the store advances it on successful update.
    /// </summary>
    StoreAdvancesVersion = 1
}
