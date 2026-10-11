namespace Ntxinh.EFCore.Bulks;

/// <summary>Options for <see cref="BulkExtensions"/> operations.</summary>
public sealed record BulkOptions
{
    /// <summary>Rows sent to SQL Server per batch; 0 (default) sends all rows as one batch. Batches still share one transaction.</summary>
    public int BatchSize { get; init; }

    /// <summary>Timeout for each SQL Server call; <see cref="TimeSpan.Zero"/> means no limit. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>Insert the entities' identity values instead of letting SQL Server assign them. Default false.</summary>
    public bool KeepIdentity { get; init; }

    /// <summary>
    /// Enforce CHECK and FOREIGN KEY constraints during the copy. Default true; turning it off is faster
    /// but leaves the table's foreign keys marked untrusted in SQL Server.
    /// </summary>
    public bool CheckConstraints { get; init; } = true;
}
