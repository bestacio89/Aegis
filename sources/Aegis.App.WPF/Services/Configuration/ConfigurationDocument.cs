using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace Aegis.Wpf.Services.Configuration;

public sealed class ConfigurationDocument : INotifyPropertyChanged
{
    private string? _loadError;
    private JsonObject? _root;

    public ConfigurationDocument(
        string fileName,
        string title,
        string description,
        string filePath,
        JsonObject? root,
        string? loadError = null)
    {
        FileName = fileName;
        Title = title;
        Description = description;
        FilePath = filePath;
        _root = root;
        _loadError = loadError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string FileName { get; }
    public string Title { get; }
    public string Description { get; }
    public string FilePath { get; }
    public ObservableCollection<ConfigurationSetting> Settings { get; } = new();

    public JsonObject? Root
    {
        get => _root;
        internal set => SetField(ref _root, value);
    }

    public string? LoadError
    {
        get => _loadError;
        internal set => SetField(ref _loadError, value);
    }

    public bool IsLoaded => Root is not null && string.IsNullOrWhiteSpace(LoadError);
    public bool IsDirty => Settings.Any(setting => setting.IsDirty);
    public int DirtySettingCount => Settings.Count(setting => setting.IsDirty);

    internal void SetSettings(IEnumerable<ConfigurationSetting> settings)
    {
        foreach (var existing in Settings)
        {
            existing.PropertyChanged -= OnSettingPropertyChanged;
        }

        Settings.Clear();

        foreach (var setting in settings)
        {
            setting.PropertyChanged += OnSettingPropertyChanged;
            Settings.Add(setting);
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtySettingCount));
    }

    internal void MarkSaved(JsonObject root)
    {
        Root = root;

        foreach (var setting in Settings)
        {
            setting.MarkSaved();
        }

        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DirtySettingCount));
    }

    private void OnSettingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConfigurationSetting.IsDirty)
            or nameof(ConfigurationSetting.ValueText)
            or nameof(ConfigurationSetting.Error))
        {
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(DirtySettingCount));
        }
    }

    private bool SetField<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
