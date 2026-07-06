namespace Aegis.App.Wpf.ViewModels;

public class InspectableItemViewModel
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IEnumerable<object> Properties { get; set; } = Array.Empty<object>();
}