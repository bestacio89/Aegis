using System;
using System.ComponentModel;
using System.Text.Json.Serialization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aegis.Wpf.Services.Configuration;

public enum ConfigurationValueKind
{
    String,
    Boolean,
    Number,
    Object,
    Array,
    Null
}

public sealed class ConfigurationSetting : INotifyPropertyChanged
{
    private string _valueText;
    private string _error = string.Empty;
    private string _originalText;
    private readonly Type? _expectedType;

    private static readonly JsonSerializerOptions ValidationJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigurationSetting(
        string[] pathSegments,
        ConfigurationValueKind valueKind,
        string valueText,
        string description,
        string defaultValueText,
        Type? expectedType)
    {
        PathSegments = pathSegments;
        ValueKind = valueKind;
        _valueText = valueText;
        _originalText = valueText;
        Description = description;
        DefaultValueText = defaultValueText;
        _expectedType = expectedType;
        Name = pathSegments.LastOrDefault() ?? string.Empty;
        PathDisplay = string.Join("  /  ", pathSegments.Take(Math.Max(0, pathSegments.Length - 1)));
        Validate();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string[] PathSegments { get; }
    public ConfigurationValueKind ValueKind { get; }
    public string Name { get; }
    public string PathDisplay { get; }
    public string Description { get; }
    public string DefaultValueText { get; }
    public string TypeLabel => ValueKind switch
    {
        ConfigurationValueKind.String => "TEXT",
        ConfigurationValueKind.Boolean => "BOOLEAN",
        ConfigurationValueKind.Number => "NUMBER",
        ConfigurationValueKind.Object => "JSON OBJECT",
        ConfigurationValueKind.Array => "JSON ARRAY",
        ConfigurationValueKind.Null when IsOptionalStructured => "OPTIONAL JSON",
        _ => "NULL"
    };

    public bool IsStructured =>
        ValueKind is ConfigurationValueKind.Object or ConfigurationValueKind.Array ||
        IsOptionalStructured;

    private bool IsOptionalStructured =>
        ValueKind == ConfigurationValueKind.Null &&
        _expectedType is { IsGenericType: true } &&
        (_expectedType.GetGenericTypeDefinition() == typeof(System.Collections.Generic.Dictionary<,>) ||
         _expectedType.IsArray);

    public string ValueText
    {
        get => _valueText;
        set
        {
            if (_valueText == value)
                return;

            _valueText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
            Validate();
        }
    }

    public string Error
    {
        get => _error;
        private set
        {
            if (_error == value)
                return;

            _error = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(Error);
    public bool IsDirty => !string.Equals(ValueText, _originalText, StringComparison.Ordinal);

    internal void MarkSaved()
    {
        _originalText = ValueText;
        Error = string.Empty;
        OnPropertyChanged(nameof(IsDirty));
    }

    internal bool TryCreateNode(out JsonNode? node, out string error)
    {
        node = null;
        error = string.Empty;

        try
        {
            switch (ValueKind)
            {
                case ConfigurationValueKind.String:
                    node = JsonValue.Create(ValueText);
                    return ValidateExpectedType(node, out error);

                case ConfigurationValueKind.Boolean:
                    if (!bool.TryParse(ValueText, out var boolean))
                    {
                        error = "Enter true or false.";
                        return false;
                    }

                    node = JsonValue.Create(boolean);
                    return ValidateExpectedType(node, out error);

                case ConfigurationValueKind.Number:
                    using (var numberDocument = JsonDocument.Parse(ValueText))
                    {
                        if (numberDocument.RootElement.ValueKind != JsonValueKind.Number)
                        {
                            error = "Enter a valid numeric value.";
                            return false;
                        }
                    }

                    node = JsonNode.Parse(ValueText);
                    return ValidateExpectedType(node, out error);

                case ConfigurationValueKind.Object:
                case ConfigurationValueKind.Array:
                    node = JsonNode.Parse(ValueText);
                    if (ValueKind == ConfigurationValueKind.Object && node is not JsonObject)
                    {
                        error = "This setting requires a JSON object.";
                        node = null;
                        return false;
                    }

                    if (ValueKind == ConfigurationValueKind.Array && node is not JsonArray)
                    {
                        error = "This setting requires a JSON array.";
                        node = null;
                        return false;
                    }

                    return ValidateExpectedType(node, out error);

                case ConfigurationValueKind.Null:
                    node = JsonNode.Parse(ValueText);
                    return ValidateExpectedType(node, out error);

                default:
                    error = "Unsupported configuration value type.";
                    return false;
            }
        }
        catch (JsonException ex)
        {
            error = $"Invalid JSON: {ex.Message}";
            return false;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool ValidateExpectedType(JsonNode? node, out string error)
    {
        error = string.Empty;
        if (_expectedType is null)
            return true;

        try
        {
            var json = node?.ToJsonString(ValidationJsonOptions) ?? "null";
            _ = JsonSerializer.Deserialize(json, _expectedType, ValidationJsonOptions);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or FormatException or OverflowException)
        {
            error = $"Value is incompatible with the policy model type '{_expectedType.Name}': {ex.Message}";
            return false;
        }
    }

    private void Validate()
    {
        if (TryCreateNode(out _, out var error))
        {
            Error = string.Empty;
            return;
        }

        Error = error;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
