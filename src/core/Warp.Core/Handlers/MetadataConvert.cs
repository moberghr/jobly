using System.Text.Json;

namespace Warp.Core.Handlers;

public static class MetadataConvert
{
    public static T? To<T>(object? value)
    {
        if (value == null)
        {
            return default;
        }

        if (value is T typed)
        {
            return typed;
        }

        if (value is JsonElement element)
        {
            try
            {
                return element.Deserialize<T>();
            }
            catch (JsonException)
            {
                return default;
            }
        }

        // Numeric conversions (NativeObjectConverter returns long for all integers)
        if (typeof(T) == typeof(int) || typeof(T) == typeof(int?))
        {
            return (T)(object)Convert.ToInt32(value);
        }

        if (typeof(T) == typeof(long) || typeof(T) == typeof(long?))
        {
            return (T)(object)Convert.ToInt64(value);
        }

        if (typeof(T) == typeof(double) || typeof(T) == typeof(double?))
        {
            return (T)(object)Convert.ToDouble(value);
        }

        // List<object> → T[] conversion (NativeObjectConverter returns List<object> for arrays)
        if (typeof(T).IsArray && value is System.Collections.IList list)
        {
            var elementType = typeof(T).GetElementType()!;
            var array = Array.CreateInstance(elementType, list.Count);
            for (var i = 0; i < list.Count; i++)
            {
                array.SetValue(Convert.ChangeType(list[i], elementType), i);
            }

            return (T)(object)array;
        }

        return default;
    }
}
