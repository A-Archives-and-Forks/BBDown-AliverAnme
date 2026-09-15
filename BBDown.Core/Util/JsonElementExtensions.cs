using System.Text.Json;

namespace BBDown.Core.Util;

public static class JsonElementExtensions
{
    public static string GetStringSafe(this JsonElement element, string propertyName, string defaultValue = "")
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;
        return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? defaultValue : defaultValue;
    }

    public static int GetInt32Safe(this JsonElement element, string propertyName, int defaultValue = 0)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var v))
            return v;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sv))
            return sv;
        return defaultValue;
    }

    public static long GetInt64Safe(this JsonElement element, string propertyName, long defaultValue = 0)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt64(out var v))
            return v;
        if (prop.ValueKind == JsonValueKind.String && long.TryParse(prop.GetString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var sv))
            return sv;
        return defaultValue;
    }

    public static double GetDoubleSafe(this JsonElement element, string propertyName, double defaultValue = 0)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var v))
            return v;
        if (prop.ValueKind == JsonValueKind.String && double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var sv))
            return sv;
        return defaultValue;
    }

    public static bool GetBooleanSafe(this JsonElement element, string propertyName, bool defaultValue = false)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;
        return prop.ValueKind == JsonValueKind.True || (prop.ValueKind == JsonValueKind.False ? prop.GetBoolean() : defaultValue);
    }

    public static JsonElement GetPropertySafe(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Expected JSON object, got {element.ValueKind}");
        if (!element.TryGetProperty(propertyName, out var prop))
            throw new KeyNotFoundException($"JSON property not found: '{propertyName}' (available keys: {FormatAvailableKeys(element)})");
        return prop;
    }

    /// <summary>
    /// RF-53：异常消息里的"全部键名"清单来自服务器可控响应，可含经 JSON 反转义的控制字符
    /// （\u001b ANSI 转义等），直拼消息会经 Logger/终端注入转义序列（B3-L3 同族）；大响应的
    /// 键名清单同时可把单行消息撑到极大。与 Parser.SanitizeServerText 同款：剥离控制字符 +
    /// 截断保留前 8 个键名。
    /// </summary>
    private static string FormatAvailableKeys(JsonElement element)
    {
        const int MaxKeys = 8;
        var sb = new System.Text.StringBuilder();
        int count = 0;
        foreach (var p in element.EnumerateObject())
        {
            if (count >= MaxKeys)
            {
                sb.Append(", …");
                break;
            }
            if (count > 0) sb.Append(", ");
            foreach (var ch in p.Name)
                sb.Append(char.IsControl(ch) ? ' ' : ch);
            count++;
        }
        return sb.ToString();
    }

    public static string GetValueAsStringSafe(this JsonElement element, string propertyName, string defaultValue = "")
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;
        if (!element.TryGetProperty(propertyName, out var prop))
            return defaultValue;
        return prop.ValueKind == JsonValueKind.Null || prop.ValueKind == JsonValueKind.Undefined ? defaultValue : prop.ToString();
    }

    public static IEnumerable<JsonElement> EnumerateArraySafe(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return Enumerable.Empty<JsonElement>();
        if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return Enumerable.Empty<JsonElement>();
        return prop.EnumerateArray();
    }

    public static JsonElement? TryGetPropertySafe(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;
        return element.TryGetProperty(propertyName, out var prop) ? prop : null;
    }
}
