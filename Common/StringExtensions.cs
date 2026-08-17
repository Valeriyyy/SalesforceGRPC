using Newtonsoft.Json;

namespace Common;

public static class StringExtensions {
    public static string ToJson(this object obj) {
        return JsonConvert.SerializeObject(obj, Formatting.Indented);
    }
}