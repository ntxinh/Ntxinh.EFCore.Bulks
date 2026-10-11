namespace Ntxinh.EFCore.Bulks;

/// <summary>Options for <see cref="SqlTemplateExtensions.GetInsertSql{T}"/>.</summary>
public sealed record InsertSqlOptions
{
    /// <summary>
    /// CLR property names whose value is written as <c>GETUTCDATE()</c> instead of a parameter.
    /// Names the entity does not have are ignored, so one list can serve every entity.
    /// </summary>
    public IReadOnlyCollection<string> UtcNowColumns { get; init; } = [];
}
