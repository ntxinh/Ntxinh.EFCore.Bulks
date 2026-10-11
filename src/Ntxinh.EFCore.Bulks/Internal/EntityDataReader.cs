using System.Collections;
using System.Data.Common;

namespace Ntxinh.EFCore.Bulks.Internal;

/// <summary>Streams entities to SqlBulkCopy one row at a time; no DataTable copy is built.</summary>
internal sealed class EntityDataReader(IEnumerable rows, IReadOnlyList<ColumnMap> columns) : DbDataReader
{
    private readonly IEnumerator rowEnumerator = rows.GetEnumerator();
    private object? current;

    public int RowCount { get; private set; }

    public override int FieldCount => columns.Count;

    public override bool HasRows => true;

    public override bool IsClosed => false;

    public override int RecordsAffected => -1;

    public override int Depth => 0;

    public override object this[int ordinal] => GetValue(ordinal);

    public override object this[string name] => GetValue(GetOrdinal(name));

    public override bool Read()
    {
        if (!rowEnumerator.MoveNext())
        {
            current = null;
            return false;
        }

        current = rowEnumerator.Current;
        RowCount++;
        return true;
    }

    public override object GetValue(int ordinal) =>
        columns[ordinal].Read(current ?? throw new InvalidOperationException("Read() has not returned a row.")) ?? DBNull.Value;

    public override int GetValues(object[] values)
    {
        var count = Math.Min(values.Length, columns.Count);
        for (var i = 0; i < count; i++)
        {
            values[i] = GetValue(i);
        }

        return count;
    }

    public override bool IsDBNull(int ordinal) => GetValue(ordinal) is DBNull;

    public override string GetName(int ordinal) => columns[ordinal].ColumnName;

    public override int GetOrdinal(string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i].ColumnName, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(name), name, "No such column.");
    }

    public override Type GetFieldType(int ordinal) => columns[ordinal].ProviderType;

    public override string GetDataTypeName(int ordinal) => GetFieldType(ordinal).Name;

    public override bool GetBoolean(int ordinal) => (bool)GetValue(ordinal);

    public override byte GetByte(int ordinal) => (byte)GetValue(ordinal);

    public override char GetChar(int ordinal) => (char)GetValue(ordinal);

    public override DateTime GetDateTime(int ordinal) => (DateTime)GetValue(ordinal);

    public override decimal GetDecimal(int ordinal) => (decimal)GetValue(ordinal);

    public override double GetDouble(int ordinal) => (double)GetValue(ordinal);

    public override float GetFloat(int ordinal) => (float)GetValue(ordinal);

    public override Guid GetGuid(int ordinal) => (Guid)GetValue(ordinal);

    public override short GetInt16(int ordinal) => (short)GetValue(ordinal);

    public override int GetInt32(int ordinal) => (int)GetValue(ordinal);

    public override long GetInt64(int ordinal) => (long)GetValue(ordinal);

    public override string GetString(int ordinal) => (string)GetValue(ordinal);

    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        throw new NotSupportedException();

    public override bool NextResult() => false;

    public override IEnumerator GetEnumerator() => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            (rowEnumerator as IDisposable)?.Dispose();
        }

        base.Dispose(disposing);
    }
}
