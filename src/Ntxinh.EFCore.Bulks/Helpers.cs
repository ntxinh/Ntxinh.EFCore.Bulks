namespace Ntxinh.EFCore.Bulks;

public static class Helpers
{
    public static string SpecialRuleForColumnValue(string columnName)
    {
        if (IsDateColumn(columnName))
            return "GETUTCDATE()";
        return $"@{columnName}";
    }

    public static bool IsDateColumn(string columnName)
    {
        switch (columnName)
        {
            case "CreatedAt":
            case "UpdatedAt":
            case "CreatedOn":
            case "UpdatedOn":
                return true;
            default:
                return false;
        }
    }
}
