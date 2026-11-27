namespace Aegis.Wpf.ViewModels.Architecture;

public sealed class SystemLandscapeRegionViewModel
{
    public SystemLandscapeRegionViewModel(
        string id,
        string name,
        string role,
        string description,
        IEnumerable<SystemLandscapeRegionViewModel> children,
        IEnumerable<SystemLandscapeCodeContainerViewModel> codeContainers,
        bool hasMissingParent)
    {
        Id = id;
        Name = name;
        Role = role;
        Description = description;
        Children = Array.AsReadOnly(children.ToArray());
        CodeContainers = Array.AsReadOnly(codeContainers.ToArray());
        HasMissingParent = hasMissingParent;
    }

    public string Id { get; }
    public string Name { get; }
    public string Role { get; }
    public string Description { get; }
    public IReadOnlyList<SystemLandscapeRegionViewModel> Children { get; }
    public IReadOnlyList<SystemLandscapeCodeContainerViewModel> CodeContainers { get; }
    public bool HasMissingParent { get; }
}
