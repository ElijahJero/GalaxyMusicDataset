using System.Text.Json;

namespace GalaxyMusicDataset.Services.Http;

public static class JsonElementExtensions
{
    public static IEnumerable<JsonElement> EnumerateFlexibleArray(this JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                yield return item;
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            yield return element;
        }
    }

    public static string? GetFlexibleText(this JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.Object when element.TryGetProperty("#text", out var text) => text.GetString(),
            JsonValueKind.Object when element.TryGetProperty("name", out var name) => name.GetString(),
            JsonValueKind.Object when element.TryGetProperty("title", out var title) => title.GetString(),
            _ => null
        };
    }

    public static string? GetPropertyString(this JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.ToString(),
            JsonValueKind.Null => null,
            _ => value.GetFlexibleText()
        };
    }

    public static int? GetPropertyInt(this JsonElement element, string name)
    {
        var raw = element.GetPropertyString(name);
        return int.TryParse(raw, out var n) ? n : null;
    }

    public static long? GetPropertyLong(this JsonElement element, string name)
    {
        var raw = element.GetPropertyString(name);
        return long.TryParse(raw, out var n) ? n : null;
    }
}
