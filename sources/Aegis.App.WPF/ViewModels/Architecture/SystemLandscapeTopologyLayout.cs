using System.Windows;
using System.Windows.Media;

namespace Aegis.Wpf.ViewModels.Architecture;

/// <summary>
/// Deterministic hierarchy-first placement and orthogonal routing for the system landscape.
/// Relationship density never participates in node placement.
/// </summary>
public static class SystemLandscapeTopologyLayout
{
    private const double Margin = 36;
    private const double NodeWidth = 220;
    private const double NodeHeight = 76;
    private const double NodeGap = 22;
    private const double RowGap = 32;
    private const double RegionGap = 44;
    private const double RegionHeaderHeight = 42;

    public static Size Arrange(
        IReadOnlyList<SystemLandscapeTopologyNodeViewModel> nodes,
        IReadOnlyList<SystemLandscapeTopologyEdgeViewModel> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);

        var regions = nodes.Where(node => node.IsRegion)
            .OrderBy(node => node.HierarchyDepth)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToDictionary(node => node.Id, StringComparer.Ordinal);

        var parentById = regions.Values.ToDictionary(
            region => region.Id,
            region => region.ParentRegionId is not null && regions.ContainsKey(region.ParentRegionId)
                ? region.ParentRegionId
                : null,
            StringComparer.Ordinal);

        var cyclic = FindCyclicRegions(parentById, regions);
        foreach (var id in cyclic)
            parentById[id] = null;

        var childrenByParent = parentById
            .Where(pair => pair.Value is not null)
            .GroupBy(pair => pair.Value!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(pair => regions[pair.Key])
                    .OrderBy(region => region.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(region => region.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var codeByParent = nodes.Where(node => !node.IsRegion && node.ParentRegionId is not null
                && regions.ContainsKey(node.ParentRegionId))
            .GroupBy(node => node.ParentRegionId!, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(node => node.Category, StringComparer.Ordinal)
                    .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(node => node.Id, StringComparer.Ordinal)
                    .ToArray(),
                StringComparer.Ordinal);

        var measured = new Dictionary<string, Size>(StringComparer.Ordinal);
        Size MeasureRegion(string regionId)
        {
            if (measured.TryGetValue(regionId, out var cached))
                return cached;

            var code = codeByParent.GetValueOrDefault(regionId) ?? [];
            var childRegions = childrenByParent.GetValueOrDefault(regionId) ?? [];
            var columns = code.Length == 0 ? 0 : Math.Min(4, code.Length);
            var rows = columns == 0 ? 0 : (int)Math.Ceiling(code.Length / (double)columns);
            var codeWidth = columns == 0 ? 0 : columns * NodeWidth + (columns - 1) * NodeGap;
            var codeHeight = rows == 0 ? 0 : rows * NodeHeight + (rows - 1) * RowGap;

            var childSizes = childRegions.Select(child => MeasureRegion(child.Id)).ToArray();
            var childWidth = childSizes.Length == 0 ? 0 : childSizes.Max(size => size.Width);
            var childHeight = childSizes.Sum(size => size.Height)
                + Math.Max(0, childSizes.Length - 1) * RegionGap;

            var contentWidth = Math.Max(codeWidth, childWidth);
            var contentHeight = codeHeight + (codeHeight > 0 && childHeight > 0 ? RegionGap : 0) + childHeight;
            var width = Math.Max(NodeWidth, contentWidth) + 2 * Margin;
            var height = RegionHeaderHeight + 2 * Margin + contentHeight;
            var size = new Size(width, height);
            measured.Add(regionId, size);
            return size;
        }

        void PlaceRegion(string regionId, double left, double top)
        {
            var region = regions[regionId];
            var size = MeasureRegion(regionId);
            region.Left = left;
            region.Top = top;
            region.Width = size.Width;
            region.Height = size.Height;

            var code = codeByParent.GetValueOrDefault(regionId) ?? [];
            var columns = code.Length == 0 ? 0 : Math.Min(4, code.Length);
            var rows = columns == 0 ? 0 : (int)Math.Ceiling(code.Length / (double)columns);
            var codeHeight = rows == 0 ? 0 : rows * NodeHeight + (rows - 1) * RowGap;

            for (var index = 0; index < code.Length; index++)
            {
                var row = index / columns;
                var column = index % columns;
                code[index].Left = left + Margin + column * (NodeWidth + NodeGap);
                code[index].Top = top + RegionHeaderHeight + Margin + row * (NodeHeight + RowGap);
            }

            var childTop = top + RegionHeaderHeight + Margin + codeHeight
                + (codeHeight > 0 && (childrenByParent.GetValueOrDefault(regionId)?.Length ?? 0) > 0
                    ? RegionGap
                    : 0);
            foreach (var child in childrenByParent.GetValueOrDefault(regionId) ?? [])
            {
                PlaceRegion(child.Id, left + Margin, childTop);
                childTop += MeasureRegion(child.Id).Height + RegionGap;
            }
        }

        var roots = regions.Values
            .Where(region => parentById[region.Id] is null)
            .OrderBy(region => region.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(region => region.Id, StringComparer.Ordinal)
            .ToArray();

        var nextTop = Margin;
        foreach (var root in roots)
        {
            PlaceRegion(root.Id, Margin, nextTop);
            nextTop += MeasureRegion(root.Id).Height + RegionGap;
        }

        PlaceUnowned(nodes, regions.Keys.ToHashSet(StringComparer.Ordinal), ref nextTop);

        var positions = nodes.ToDictionary(
            node => node.Id,
            node => new Rect(node.Left, node.Top, node.Width, node.Height),
            StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            edge.Route = CreateOrthogonalRoute(edge, positions);
            edge.ArrowHead = CreateArrowHead(edge, positions);
        }

        var right = nodes.Count == 0 ? Margin : nodes.Max(node => node.Left + node.Width) + Margin;
        var bottom = nodes.Count == 0 ? Margin : nodes.Max(node => node.Top + node.Height) + Margin;
        return new Size(Math.Max(1, right), Math.Max(1, bottom));
    }

    private static HashSet<string> FindCyclicRegions(
        IReadOnlyDictionary<string, string?> parentById,
        IReadOnlyDictionary<string, SystemLandscapeTopologyNodeViewModel> regions)
    {
        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var region in regions.Values)
        {
            var path = new List<string>();
            var positions = new Dictionary<string, int>(StringComparer.Ordinal);
            var currentId = region.Id;

            while (regions.ContainsKey(currentId))
            {
                if (positions.TryGetValue(currentId, out var cycleStart))
                {
                    foreach (var id in path.Skip(cycleStart))
                        cyclic.Add(id);
                    break;
                }

                positions[currentId] = path.Count;
                path.Add(currentId);
                if (parentById[currentId] is not { } parentId)
                    break;
                currentId = parentId;
            }
        }

        return cyclic;
    }

    private static void PlaceUnowned(
        IReadOnlyList<SystemLandscapeTopologyNodeViewModel> nodes,
        IReadOnlySet<string> regionIds,
        ref double nextTop)
    {
        var unowned = nodes.Where(node => !node.IsRegion
                && (node.ParentRegionId is null || !regionIds.Contains(node.ParentRegionId)))
            .OrderBy(node => node.Category, StringComparer.Ordinal)
            .ThenBy(node => node.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();

        foreach (var category in unowned.GroupBy(node => node.Category, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var index = 0;
            foreach (var node in category)
            {
                node.Left = Margin + index * (NodeWidth + NodeGap);
                node.Top = nextTop + RegionHeaderHeight + Margin;
                index++;
            }

            nextTop += RegionHeaderHeight + Margin + NodeHeight + RegionGap;
        }
    }

    private static Geometry CreateArrowHead(
        SystemLandscapeTopologyEdgeViewModel edge,
        IReadOnlyDictionary<string, Rect> positions)
    {
        if (!positions.TryGetValue(edge.SourceNodeId, out var source)
            || !positions.TryGetValue(edge.TargetNodeId, out var target))
            return Geometry.Empty;

        var forward = source.Left + source.Width / 2 <= target.Left + target.Width / 2;
        var tip = new Point(forward ? target.Left : target.Right, target.Top + target.Height / 2);
        var basePoint = new Point(tip.X + (forward ? -9 : 9), tip.Y);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(tip, true, true);
            context.LineTo(new Point(basePoint.X, basePoint.Y - 5), true, false);
            context.LineTo(new Point(basePoint.X, basePoint.Y + 5), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    private static Geometry CreateOrthogonalRoute(
        SystemLandscapeTopologyEdgeViewModel edge,
        IReadOnlyDictionary<string, Rect> positions)
    {
        if (!positions.TryGetValue(edge.SourceNodeId, out var source)
            || !positions.TryGetValue(edge.TargetNodeId, out var target))
            return Geometry.Empty;

        var forward = source.Left + source.Width / 2 <= target.Left + target.Width / 2;
        var start = new Point(forward ? source.Right : source.Left, source.Top + source.Height / 2);
        var end = new Point(forward ? target.Left : target.Right, target.Top + target.Height / 2);
        var middleX = (start.X + end.X) / 2;

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, false, false);
            context.LineTo(new Point(middleX, start.Y), true, false);
            context.LineTo(new Point(middleX, end.Y), true, false);
            context.LineTo(end, true, false);
        }

        geometry.Freeze();
        return geometry;
    }
}