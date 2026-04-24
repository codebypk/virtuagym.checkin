using System.Text.Json;
using System.Text.Json.Serialization;

namespace Virtuagym.API.Serialization;

public class JsonSerializerAdapter
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Serialize(object obj)
    {
        return JsonSerializer.Serialize(obj, _options);
    }

    public T Deserialize<T>(string input)
    {
        return JsonSerializer.Deserialize<T>(input, _options);
    }

    public object DeserializeObject(string input)
    {
        return JsonSerializer.Deserialize<object>(input, _options);
    }
}
