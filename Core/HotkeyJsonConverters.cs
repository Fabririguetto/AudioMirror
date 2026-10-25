using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace AudioMirror.Core;

public sealed class ModifierKeysJsonConverter : JsonConverter<ModifierKeys>
{
    public override ModifierKeys Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return (ModifierKeys)reader.GetInt32();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? str = reader.GetString();
            if (string.IsNullOrWhiteSpace(str)) return ModifierKeys.None;

            ModifierKeys result = ModifierKeys.None;
            var parts = str.Split(new[] { '+', ',', '|', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Control;
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Alt;
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Shift;
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                    result |= ModifierKeys.Windows;
            }
            return result;
        }

        return ModifierKeys.Control | ModifierKeys.Alt;
    }

    public override void Write(Utf8JsonWriter writer, ModifierKeys value, JsonSerializerOptions options)
    {
        var parts = new List<string>();
        if (value.HasFlag(ModifierKeys.Control)) parts.Add("Control");
        if (value.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (value.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (value.HasFlag(ModifierKeys.Windows)) parts.Add("Windows");

        writer.WriteStringValue(string.Join("+", parts));
    }
}

public sealed class KeyJsonConverter : JsonConverter<Key>
{
    public override Key Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return (Key)reader.GetInt32();
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            string? str = reader.GetString();
            if (Enum.TryParse<Key>(str, ignoreCase: true, out var key))
            {
                return key;
            }
        }

        return Key.A;
    }

    public override void Write(Utf8JsonWriter writer, Key value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToString());
    }
}