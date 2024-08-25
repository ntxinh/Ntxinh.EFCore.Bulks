using System.Data;

namespace Ntxinh.EFCore.Bulks;

public static class DataTableHelper
{
    public static DataTable CreateDataTable<T>()
    {
        return CreateDataTable<T>([]);
    }

    public static DataTable CreateDataTable<T>(T entity)
    {
        return CreateDataTable<T>([entity]);
    }

    public static DataTable CreateDataTable<T>(IEnumerable<T> item = null)
    {
        var type = typeof(T);
        var properties = type.GetProperties();

        var dataTable = new DataTable
        {
            TableName = type.Name,
        };

        foreach (var info in properties)
        {
            if (info.Name.Equals("DomainEvents", StringComparison.OrdinalIgnoreCase))
                continue;
            dataTable.Columns.Add(new DataColumn(info.Name, Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType));
        }

        if (item is not null && item.Any())
        {
            foreach (T entity in item)
            {
                object[] values = new object[properties.Length];
                for (int i = 0; i < properties.Length; i++)
                {
                    values[i] = properties[i].GetValue(entity);
                }

                dataTable.Rows.Add(values);
            }
        }

        return dataTable;
    }
}
