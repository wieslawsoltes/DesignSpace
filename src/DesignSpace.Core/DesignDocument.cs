using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace DesignSpace.Core;

public sealed record DesignNode
{
    public const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    public static readonly string NameKey = "{" + XamlNamespace + "}Name";
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Type { get; init; } = "Rectangle";
    public string Namespace { get; init; } = PresentationNamespace;
    public ImmutableDictionary<string,string> Properties { get; init; } = ImmutableDictionary<string,string>.Empty;
    public ImmutableArray<DesignNode> Children { get; init; } = [];
    public ImmutableArray<string> PropertyElements { get; init; } = [];
    public string TextContent { get; init; } = "";
    public bool IsLocked { get; init; }
    public double Rotation { get; init; }
    public string Name => Get(NameKey, Get("Name", Type));
    public string Get(string key, string fallback = "") => Properties.TryGetValue(key, out var value) ? value : fallback;
    public double Number(string key, double fallback = 0) => Numbers.Parse(Get(key), fallback);
    public bool Visible => Get("Visibility", "Visible") != "Collapsed";
    public DesignNode Set(string key, string value) => this with { Properties = Properties.SetItem(key,value) };
    public DesignNode Set(string key, double value) => Set(key, Numbers.Format(value));
    public IEnumerable<DesignNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Children) foreach (var node in child.DescendantsAndSelf()) yield return node;
    }
    public DesignNode? Find(Guid id) => Id == id ? this : Children.Select(c => c.Find(id)).FirstOrDefault(n => n is not null);
    public DesignNode? ParentOf(Guid id) => Children.Any(c => c.Id == id) ? this : Children.Select(c => c.ParentOf(id)).FirstOrDefault(n => n is not null);
    public DesignNode Update(Guid id, Func<DesignNode,DesignNode> transform)
    {
        if (Id == id) return transform(this);
        var changed = false;
        var children = Children.Select(c => { var next = c.Update(id,transform); changed |= !ReferenceEquals(c,next); return next; }).ToImmutableArray();
        return changed ? this with { Children = children } : this;
    }
    public DesignNode Remove(IReadOnlySet<Guid> ids) => this with { Children = Children.Where(c => !ids.Contains(c.Id)).Select(c => c.Remove(ids)).ToImmutableArray() };
    public static DesignNode Create(string type, string name, double x = 0, double y = 0, double width = 120, double height = 80) =>
        new DesignNode { Type = type }.Set(NameKey,name).Set("Canvas.Left",x).Set("Canvas.Top",y).Set("Width",width).Set("Height",height);
}
public sealed record AnimationKey(double Time, double Value, string Easing = "Linear");
public sealed record AnimationTrack(Guid TargetId, string Property, ImmutableArray<AnimationKey> Keys);
public sealed record DesignStoryboard(Guid Id, string Name, double Duration, ImmutableArray<AnimationTrack> Tracks, bool Loop = false);
public sealed record StateSetter(Guid TargetId, string Property, string Value);
public sealed record DesignState(string Name, ImmutableArray<StateSetter> Setters);
public sealed record DesignDocument
{
    public int FormatVersion { get; init; } = 1;
    public string Title { get; init; } = "MainPage.xaml";
    public DesignNode Root { get; init; } = DesignNode.Create("Canvas","LayoutRoot",0,0,960,640).Set("Background","#FFFFFFFF");
    public ImmutableArray<DesignStoryboard> Storyboards { get; init; } = [];
    public ImmutableArray<DesignState> States { get; init; } = [];
    public static DesignDocument Empty() => new();
}
public static partial class DocumentValidator
{
    public const int MaxNodes = 20000;
    public const int MaxDepth = 64;
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
    public static void Validate(DesignDocument doc)
    {
        if (doc.FormatVersion != 1) throw new InvalidDataException("Unsupported DesignSpace document version.");
        var ids = new HashSet<Guid>(); var names = new HashSet<string>(StringComparer.Ordinal); var count = 0;
        void Walk(DesignNode n, int depth)
        {
            if (++count > MaxNodes || depth > MaxDepth) throw new InvalidDataException("The document exceeds the node or nesting limit.");
            if (!ids.Add(n.Id)) throw new InvalidDataException("Duplicate element identity.");
            var name = n.Get(DesignNode.NameKey,n.Get("Name"));
            if (name.Length > 0 && (!Identifier().IsMatch(name) || !names.Add(name))) throw new InvalidDataException("Element names must be unique XAML identifiers: " + name);
            foreach (var key in new[] {"Width","Height","Canvas.Left","Canvas.Top","Opacity","FontSize","StrokeThickness"})
            {
                var value = n.Get(key); if (value.Length == 0 || value == "Auto" || value.StartsWith('{')) continue;
                if (!double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) throw new InvalidDataException("Invalid numeric value for " + key);
                if ((key is "Width" or "Height" or "FontSize" or "StrokeThickness") && number < 0) throw new InvalidDataException(key + " cannot be negative.");
                if (key == "Opacity" && (number < 0 || number > 1)) throw new InvalidDataException("Opacity must be between zero and one.");
            }
            if (!double.IsFinite(n.Rotation)) throw new InvalidDataException("Invalid rotation.");
            foreach (var child in n.Children) Walk(child,depth+1);
        }
        Walk(doc.Root,0);
        foreach (var board in doc.Storyboards)
        {
            if (board.Duration <= 0 || !double.IsFinite(board.Duration)) throw new InvalidDataException("Storyboard duration must be positive.");
            foreach (var track in board.Tracks)
            {
                if (!ids.Contains(track.TargetId)) throw new InvalidDataException("Animation references a missing element.");
                if (track.Keys.Any(k => !double.IsFinite(k.Time) || !double.IsFinite(k.Value) || k.Time < 0 || k.Time > board.Duration)) throw new InvalidDataException("Invalid animation keyframe.");
                if (track.Keys.GroupBy(k => k.Time).Any(g => g.Count() > 1)) throw new InvalidDataException("Duplicate keyframe time.");
            }
        }
        foreach (var state in doc.States) if (state.Setters.Any(s => !ids.Contains(s.TargetId))) throw new InvalidDataException("State references a missing element.");
    }
}
