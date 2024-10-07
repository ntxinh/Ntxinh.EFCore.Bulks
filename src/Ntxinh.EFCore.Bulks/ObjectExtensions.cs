using System.Reflection;

namespace Ntxinh.EFCore.Bulks;

public static class ObjectExtensions
{
    public static bool HasProperty(this object obj, string propertyName)
    {
        return obj.GetType().GetProperty(propertyName) != null;
    }

    public static object? GetPropertyValue(this object obj, string propertyName)
    {
        return obj.GetType().GetProperty(propertyName).GetValue(obj);
    }

    public static void SetPropertyValue(this object obj, string propertyName, object value)
    {
        // Get the type of the object
        Type type = obj.GetType();

        // Get the property by name
        PropertyInfo property = type.GetProperty(propertyName);

        if (property != null && property.CanWrite)
        {
            // Get the property type
            Type propertyType = property.PropertyType;

            // Check if the property type is nullable and get the underlying type
            Type underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;

            // Convert the value to the correct type and set the property value
            object safeValue = value == null ? null : Convert.ChangeType(value, underlyingType);
            property.SetValue(obj, safeValue, null);
        }
        else
        {
            Console.WriteLine($"Property {propertyName} not found or not writable.");
        }
    }
}
