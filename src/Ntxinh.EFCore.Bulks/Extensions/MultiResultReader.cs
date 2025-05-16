using System.Data.Common;
using System.Reflection;

namespace Ntxinh.EFCore.Bulks;

/// <summary>
/// A Dapper-like wrapper for DbDataReader to read multiple result sets.
/// </summary>
public class MultiResultReader : IDisposable
{
    private readonly DbDataReader _reader;
    private bool _disposed;

    public MultiResultReader(DbDataReader reader)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
    }

    /// <summary>
    /// Reads the current result set and maps it to a collection of type T.
    /// </summary>
    /// <typeparam name="T">The type to map the result set to.</typeparam>
    /// <returns>An IEnumerable of T representing the current result set.</returns>
    public async Task<IEnumerable<T>> ReadAsync<T>(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MultiResultReader));
        }

        var results = new List<T>();
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite)
            .ToArray();
        var columnNames = Enumerable.Range(0, _reader.FieldCount)
            .Select(i => _reader.GetName(i))
            .ToArray();

        while (await _reader.ReadAsync(cancellationToken))
        {
            var item = Activator.CreateInstance<T>();
            for (int i = 0; i < columnNames.Length; i++)
            {
                var prop = properties.FirstOrDefault(p => p.Name.Equals(columnNames[i], StringComparison.OrdinalIgnoreCase));
                if (prop != null && !_reader.IsDBNull(i))
                {
                    var value = _reader.GetValue(i);
                    if (value != null && prop.PropertyType.IsAssignableFrom(value.GetType()))
                    {
                        prop.SetValue(item, value);
                    }
                    else if (value != null)
                    {
                        // Handle type conversion if needed (e.g., int to long)
                        try
                        {
                            var convertedValue = Convert.ChangeType(value, prop.PropertyType);
                            prop.SetValue(item, convertedValue);
                        }
                        catch (InvalidCastException)
                        {
                            // Skip incompatible types
                        }
                    }
                }
            }
            results.Add(item);
        }

        return results;
    }

    /// <summary>
    /// Advances to the next result set.
    /// </summary>
    /// <returns>True if there is another result set, false otherwise.</returns>
    public async Task<bool> NextResultAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MultiResultReader));
        }

        return await _reader.NextResultAsync(cancellationToken);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _reader.Dispose();
            _disposed = true;
        }
    }
}
