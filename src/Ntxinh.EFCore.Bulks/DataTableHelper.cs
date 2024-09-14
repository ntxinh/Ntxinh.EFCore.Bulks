using System.Data;

namespace Ntxinh.EFCore.Bulks;

public static class DataTableHelper
{
    public static DataTable CreateDataTable<T>(params string[] excludeColumns)
    {
        return CreateDataTable<T>([], excludeColumns);
    }

    public static DataTable CreateDataTable<T>(T entity, params string[] excludeColumns)
    {
        if (entity is null)
        {
            return CreateDataTable<T>([], excludeColumns);
        }
        return CreateDataTable<T>([entity], excludeColumns);
    }

    public static DataTable CreateDataTable<T>(IEnumerable<T> item, params string[] excludeColumns)
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
            MinimumCapacity = item.Count(),
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
