using System;

namespace Ntxinh.EFCore.Bulks;

public static class TypeDefaultValue
{
    public static object GetDefaultValue(string typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentNullException(nameof(typeName));

        // Resolve the type from the string
        Type type = Type.GetType(typeName, throwOnError: false);
        if (type == null)
        {
            // Handle nullable types (e.g., System.Nullable<System.Int32>)
            if (typeName.StartsWith("System.Nullable`1[[", StringComparison.Ordinal))
            {
                // Extract the underlying type name
                string underlyingTypeName = ExtractUnderlyingTypeName(typeName);
                type = Type.GetType(underlyingTypeName);
                if (type != null)
                {
                    // For nullable types, default is null
                    return null;
                }
            }
            throw new ArgumentException($"Type '{typeName}' could not be resolved.", nameof(typeName));
        }

        // Handle nullable types directly (e.g., Nullable<int>)
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            return null;
        }

        // Return default value for the type
        return GetDefaultForType(type);
    }

    private static string ExtractUnderlyingTypeName(string nullableTypeName)
    {
        // Example input: "System.Nullable`1[[System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089]]"
        // Extract the part after "[[" and before ","
        int start = nullableTypeName.IndexOf("[[") + 2;
        int end = nullableTypeName.IndexOf(",");
        if (start < 2 || end == -1)
            throw new ArgumentException("Invalid nullable type format.", nameof(nullableTypeName));

        return nullableTypeName.Substring(start, end - start);
    }

    private static object GetDefaultForType(Type type)
    {
        // Value types (int, bool, decimal, etc.) get their default (0, false, 0.0, etc.)
        // Reference types and nullable types get null
        if (type.IsValueType)
        {
            return Activator.CreateInstance(type);
        }
        return null;
    }
}
