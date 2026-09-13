using Dapper;
using System.Data;
using System.Text.Json;

namespace Database.Utilities;

/// <summary>
/// Maps a jsonb/text column onto a <see cref="Dictionary{TKey, TValue}"/> of strings, so Dapper can read and
/// write it like any other column instead of going through an intermediate row type.
/// </summary>
/// <remarks>
/// Registered once, globally, alongside <see cref="SqlTimeOnlyTypeHandler"/> — this is that same pattern
/// applied to a JSON document instead of a time value. Global registration means every
/// <c>Dictionary&lt;string, string&gt;</c> property Dapper ever maps goes through this handler, not only the
/// one it was written for; that is an acceptable trade for not hand-rolling a row-to-model shim per table.
/// </remarks>
public class JsonStringDictionaryTypeHandler : SqlMapper.TypeHandler<Dictionary<string, string>> {
    public override void SetValue(IDbDataParameter parameter, Dictionary<string, string>? value) {
        parameter.Value = JsonSerializer.Serialize(value ?? []);
    }

    public override Dictionary<string, string> Parse(object value) {
        if (value is string json) {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }

        throw new InvalidOperationException(
            $"Cannot convert {value.GetType().FullName} to Dictionary<string, string>.");
    }
}
