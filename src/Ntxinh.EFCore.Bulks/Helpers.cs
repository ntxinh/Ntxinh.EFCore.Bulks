namespace Ntxinh.EFCore.Bulks;

public static class Helpers
{
    public static string SpecialRuleForColumnValue(string columnName)
    {
        switch (columnName)
        {
            case "CreatedAt":
            case "UpdatedAt":
            case "CreatedOn":
            case "UpdatedOn":
                return "GETUTCDATE()";
            default:
                return $"@{columnName}";
        }
    }
}
