using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace DesignSpace.Core;

public sealed record DesignNode
{
    public const string PresentationNamespace="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    public const string XamlNamespace="http://schemas.microsoft.com/winfx/2006/xaml";
    public static readonly string NameKey="{"+XamlNamespace+"}Name";
    public Guid Id { get; init; }=Guid.NewGuid();
    public string Type { get; init; }="Rectangle";
    public string Namespace { get; init; }=PresentationNamespace;
    public ImmutableDictionary<string,string> Properties { get; init; }=ImmutableDictionary<string,string>.Empty;
    public ImmutableArray<DesignNode> Children { get; init; }=[];
    public ImmutableArray<string> PropertyElements { get; init; }=[];
    public string TextContent { get; init; }="";
    public bool IsLocked { get; init; }
    public double Rotation { get; init; }
    public string Name=>Get(NameKey,Get("Name",Type));
    public string Get(string key,string fallback="")=>Properties.TryGetValue(key,out var value) ? value : fallback;
    public double Number(string key,double fallback=0)=>Numbers.Parse(Get(key),fallback);
    public bool Visible=>Get("Visibility","Visible")!="Collapsed";
    public DesignNode Set(string key,string value)=>Properties.TryGetValue(key,out var old) && old==value ? this : this with { Properties=Properties.SetItem(key,value) };
    public DesignNode Set(string key,double value)=>Set(key,Numbers.Format(value));
    public IEnumerable<DesignNode> DescendantsAndSelf()
    {
        yield return this; foreach(var child in Children) foreach(var node in child.DescendantsAndSelf()) yield return node;
    }
    public DesignNode? Find(Guid id)=>DesignIndex.For(this).Find(id);
    public DesignNode? ParentOf(Guid id)=>DesignIndex.For(this).ParentOf(id);
    public DesignNode Update(Guid id,Func<DesignNode,DesignNode> transform)=>DesignIndex.For(this).Transform(new HashSet<Guid>{id},transform);
    public DesignNode Remove(IReadOnlySet<Guid> ids)=>DesignTree.Remove(this,ids);
    public static DesignNode Create(string type,string name,double x=0,double y=0,double width=120,double height=80)=>new DesignNode { Type=type }.Set(NameKey,name).Set("Canvas.Left",x).Set("Canvas.Top",y).Set("Width",width).Set("Height",height);
}
public sealed partial record AnimationKey(double Time,double Value,string Easing="Linear");
public sealed partial record AnimationTrack(Guid TargetId,string Property,ImmutableArray<AnimationKey> Keys);
public sealed record DesignStoryboard(Guid Id,string Name,double Duration,ImmutableArray<AnimationTrack> Tracks,bool Loop=false)
{
    public bool AutoReverse { get; init; }
    public double BeginTime { get; init; }
    public double SpeedRatio { get; init; }=1;
    public double RepeatCount { get; init; }=1;
    public double? RepeatDuration { get; init; }
    public string FillBehavior { get; init; }="HoldEnd";
}
public sealed record StateSetter(Guid TargetId,string Property,string Value);
public sealed partial record DesignState(string Name,ImmutableArray<StateSetter> Setters);
public sealed partial record DesignDocument
{
    public int FormatVersion { get; init; }=1;
    public string Title { get; init; }="MainPage.xaml";
    public DesignNode Root { get; init; }=DesignNode.Create("Canvas","LayoutRoot",0,0,960,640).Set("Background","#FFFFFFFF");
    public ImmutableArray<DesignStoryboard> Storyboards { get; init; }=[];
    public ImmutableArray<DesignState> States { get; init; }=[];
    public static DesignDocument Empty()=>new();
}
public static partial class DocumentValidator
{
    public const int MaxNodes=20000,MaxDepth=64;
    private const int MaxMarkupCharacters=8*1024*1024;
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex Identifier();
    private static readonly string[] NumericProperties=["Width","Height","Canvas.Left","Canvas.Top","Canvas.Right","Canvas.Bottom","Opacity","FontSize","StrokeThickness","MinWidth","MinHeight","MaxWidth","MaxHeight","Spacing","RowSpacing","ColumnSpacing"];
    public static void Validate(DesignDocument doc)
    {
        if(doc is null || doc.Root is null || doc.FormatVersion!=1) throw new InvalidDataException("Unsupported or incomplete DesignSpace document.");
        if(doc.Storyboards.IsDefault || doc.States.IsDefault) throw new InvalidDataException("Document collections must not be null.");
        var ids=new HashSet<Guid>(); var names=new HashSet<string>(StringComparer.Ordinal); var count=0; long markup=0;
        void Walk(DesignNode n,int depth)
        {
            if(n is null || ++count>MaxNodes || depth>MaxDepth) throw new InvalidDataException("Invalid node or document size/depth limit exceeded.");
            if(n.Id==Guid.Empty || !ids.Add(n.Id)) throw new InvalidDataException("Empty or duplicate element identity.");
            if(n.Properties is null || n.Children.IsDefault || n.PropertyElements.IsDefault || n.TextContent is null) throw new InvalidDataException("Node collections and text must not be null.");
            XmlConvert.VerifyNCName(n.Type);
            var name=n.Get(DesignNode.NameKey,n.Get("Name"));
            if(name.Length>0 && (!Identifier().IsMatch(name) || !names.Add(name))) throw new InvalidDataException("Element names must be unique XAML identifiers: "+name);
            foreach(var pair in n.Properties)
            {
                if(pair.Value is null) throw new InvalidDataException("Property values must not be null.");
                _=XName.Get(pair.Key); markup+=pair.Key.Length+pair.Value.Length;
            }
            foreach(var key in NumericProperties)
            {
                var value=n.Get(key); if(value.Length==0 || (value=="Auto" && key is "Width" or "Height") || value.StartsWith('{')) continue;
                if(!double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var number) || !double.IsFinite(number)) throw new InvalidDataException("Invalid numeric value for "+key);
                if(key is not ("Canvas.Left" or "Canvas.Top" or "Canvas.Right" or "Canvas.Bottom") && number<0) throw new InvalidDataException(key+" cannot be negative.");
                if(key=="Opacity" && number>1) throw new InvalidDataException("Opacity must be between zero and one.");
            }
            StrokeStyle.ValidateLiterals(n);
            foreach(var axis in new[]{"Width","Height"}) if(n.Number("Min"+axis)>n.Number("Max"+axis,double.PositiveInfinity)) throw new InvalidDataException("Minimum size exceeds maximum size.");
            if(!double.IsFinite(n.Rotation)) throw new InvalidDataException("Invalid rotation.");
            foreach(var raw in n.PropertyElements)
            {
                if(raw is null || (markup+=raw.Length)>MaxMarkupCharacters) throw new InvalidDataException("Property markup exceeds the size limit.");
                using var reader=XmlReader.Create(new StringReader(raw),new XmlReaderSettings { DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaxMarkupCharacters });
                while(reader.Read()) if(reader.Depth>MaxDepth) throw new InvalidDataException("Property markup exceeds the nesting limit.");
            }
            markup+=n.TextContent.Length; if(markup>MaxMarkupCharacters) throw new InvalidDataException("Document text exceeds the size limit.");
            foreach(var child in n.Children) Walk(child,depth+1);
        }
        Walk(doc.Root,0);
        var boardNames=new HashSet<string>(StringComparer.Ordinal);
        foreach(var board in doc.Storyboards)
        {
            if(board is null || string.IsNullOrWhiteSpace(board.Name) || !boardNames.Add(board.Name) || board.Tracks.IsDefault) throw new InvalidDataException("Invalid or duplicate storyboard.");
            if(board.Duration<=0 || !double.IsFinite(board.Duration) || board.Duration>TimeSpan.MaxValue.TotalSeconds/4) throw new InvalidDataException("Storyboard duration is out of range.");
            if(!double.IsFinite(board.BeginTime) || board.BeginTime<0 || board.BeginTime>TimeSpan.MaxValue.TotalSeconds/4 ||
               !double.IsFinite(board.SpeedRatio) || board.SpeedRatio<=0 || !double.IsFinite(board.RepeatCount) || board.RepeatCount<0 ||
               board.RepeatDuration is { } span && (!double.IsFinite(span) || span<0 || span>TimeSpan.MaxValue.TotalSeconds/4) ||
               board.FillBehavior is not ("HoldEnd" or "Stop") || board.Loop && board.RepeatDuration is not null)
                throw new InvalidDataException("Invalid storyboard timing settings.");
            if(!board.Loop && !double.IsFinite((board.RepeatDuration ?? board.Duration*(board.AutoReverse ? 2 : 1)*board.RepeatCount)/board.SpeedRatio+board.BeginTime))
                throw new InvalidDataException("Storyboard timing exceeds the supported range.");
            var tracks=new HashSet<(Guid,string)>();
            foreach(var track in board.Tracks)
            {
                if(track is null || track.Keys.IsDefault || !tracks.Add((track.TargetId,track.Property)) || !ids.Contains(track.TargetId)) throw new InvalidDataException("Invalid, duplicate or dangling animation track.");
                AnimationValidation.ValidateTrack(track,board.Duration);
            }
        }
        ValidateStateGroups(doc);
        var stateNames=new HashSet<string>(StringComparer.Ordinal);
        foreach(var state in doc.States)
        {
            if(state is null || !Identifier().IsMatch(state.Name) || !stateNames.Add(state.Name) || state.Setters.IsDefault || state.Setters.Any(s=>s is null || !ids.Contains(s.TargetId) || string.IsNullOrWhiteSpace(s.Property) || s.Value is null) || state.Setters.GroupBy(s=>(s.TargetId,s.Property)).Any(g=>g.Count()>1)) throw new InvalidDataException("Invalid, duplicate or dangling visual state.");
        }
    }
}
