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

    public static DataTable CreateDataTable<T>(IEnumerable<T> item = null, IEnumerable<string> excludeColumns = null)
    {
        var type = typeof(T);
        var properties = type.GetProperties();
        var lstProperties = properties.ToList();

        if (excludeColumns is not null && excludeColumns.Any())
        {
            lstProperties.RemoveAll(i => excludeColumns.Any(x => x.Equals(i.Name, StringComparison.OrdinalIgnoreCase)));
        }

        var dataTable = new DataTable
        {
            TableName = type.Name,
        };

        foreach (var info in lstProperties)
        {
            dataTable.Columns.Add(new DataColumn(info.Name, Nullable.GetUnderlyingType(info.PropertyType) ?? info.PropertyType));
        }

        var numOfCols = dataTable.Columns.Count; // lstProperties.Count;

        if (item is not null && item.Any())
        {
            foreach (T entity in item)
            {
                object[] values = new object[numOfCols];
                for (int i = 0; i < numOfCols; i++)
                {
                    values[i] = lstProperties[i].GetValue(entity);
                }

                dataTable.Rows.Add(values);
            }
        }

        return dataTable;
    }
}
