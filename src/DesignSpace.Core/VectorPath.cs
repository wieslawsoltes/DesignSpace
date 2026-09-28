using System.Collections.Immutable;
using System.Globalization;
using System.Text;
namespace DesignSpace.Core;

public enum VectorSegmentKind { Line, Quadratic, Cubic, Arc }
public sealed record VectorSegment(VectorSegmentKind Kind,DPoint End)
{
    public DPoint Control1 { get; init; }
    public DPoint Control2 { get; init; }
    public DSize Radius { get; init; }
    public double Angle { get; init; }
    public bool LargeArc { get; init; }
    public bool Clockwise { get; init; }
    public static VectorSegment Line(DPoint end)=>new(VectorSegmentKind.Line,end);
    public static VectorSegment Cubic(DPoint first,DPoint second,DPoint end)=>new(VectorSegmentKind.Cubic,end){Control1=first,Control2=second};
}
public sealed record VectorFigure(DPoint Start,ImmutableArray<VectorSegment> Segments,bool Closed=false);
/// <summary>Portable, immutable finite XAML path geometry. Arc segments stay arcs until explicitly transformed.</summary>
public sealed record VectorPath(ImmutableArray<VectorFigure> Figures,bool NonZero=false)
{
    public static VectorPath Empty { get; }=new([]);
    public int SegmentCount=>Figures.Sum(f=>f.Segments.Length);
}

/// <summary>Bounded parser for finite M/L/H/V/C/S/Q/T/A/Z and F0/F1 XAML/SVG-style path syntax.</summary>
public static class VectorPathCodec
{
    public const int MaxCharacters=1_048_576,MaxSegments=8192;
    public const double MaxCoordinate=1e9;
    public static VectorPath Parse(string data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if(data.Length>MaxCharacters)throw new InvalidDataException("Path data exceeds one MiB of characters.");
        return new Parser(data).Read();
    }
    public static string Write(VectorPath path)
    {
        Validate(path);var result=new StringBuilder(path.NonZero ? "F1 " : "F0 ");
        void N(double number)=>result.Append(number.ToString("R",CultureInfo.InvariantCulture));
        void P(DPoint point){N(point.X);result.Append(',');N(point.Y);}
        foreach(var figure in path.Figures)
        {
            result.Append("M ");P(figure.Start);
            foreach(var s in figure.Segments)
            {
                result.Append(' ');
                switch(s.Kind)
                {
                    case VectorSegmentKind.Line:result.Append("L ");break;
                    case VectorSegmentKind.Quadratic:result.Append("Q ");P(s.Control1);result.Append(' ');break;
                    case VectorSegmentKind.Cubic:result.Append("C ");P(s.Control1);result.Append(' ');P(s.Control2);result.Append(' ');break;
                    case VectorSegmentKind.Arc:result.Append("A ");N(s.Radius.Width);result.Append(',');N(s.Radius.Height);result.Append(' ');N(s.Angle);result.Append(s.LargeArc ? " 1 " : " 0 ");result.Append(s.Clockwise ? "1 " : "0 ");break;
                }
                P(s.End);
            }
            result.Append(figure.Closed ? " Z " : " ");
        }
        return result.ToString().TrimEnd();
    }
    public static void Validate(VectorPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if(path.Figures.IsDefault||path.Figures.Length>MaxSegments||path.Figures.Any(f=>f is null||f.Segments.IsDefault)||path.SegmentCount>MaxSegments)throw new InvalidDataException("Invalid or excessive path figure/segment count.");
        static void Point(DPoint p){if(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||Math.Abs(p.X)>MaxCoordinate||Math.Abs(p.Y)>MaxCoordinate)throw new InvalidDataException("Path coordinates must be finite and at most one billion units.");}
        foreach(var f in path.Figures)
        {
            if(f is null||f.Segments.IsDefault)throw new InvalidDataException("Invalid path figure.");Point(f.Start);
            foreach(var s in f.Segments)
            {
                if(s is null||!Enum.IsDefined(s.Kind))throw new InvalidDataException("Invalid path segment.");Point(s.End);Point(s.Control1);Point(s.Control2);
                if(!double.IsFinite(s.Angle)||Math.Abs(s.Angle)>MaxCoordinate||!double.IsFinite(s.Radius.Width)||!double.IsFinite(s.Radius.Height)||s.Radius.Width<0||s.Radius.Height<0||s.Radius.Width>MaxCoordinate||s.Radius.Height>MaxCoordinate)throw new InvalidDataException("Invalid arc size or rotation.");
            }
        }
    }
    private sealed class Parser(string text)
    {
        private int _at,_count;
        private DPoint _current,_start,_lastCubic,_lastQuadratic;
        private char _previous;
        private readonly ImmutableArray<VectorFigure>.Builder _figures=ImmutableArray.CreateBuilder<VectorFigure>();
        private ImmutableArray<VectorSegment>.Builder? _segments;
        private bool _closed;
        private InvalidDataException Error(string detail)=>new($"Invalid path at character {_at}: {detail}");
        private void White(){while(_at<text.Length&&char.IsWhiteSpace(text[_at]))_at++;}
        private void Separator(){White();if(_at<text.Length&&text[_at]==','){_at++;White();}}
        private double Number()
        {
            Separator();var start=_at;
            if(_at<text.Length&&text[_at] is '+' or '-')_at++;
            var digits=0;while(_at<text.Length&&char.IsAsciiDigit(text[_at])){_at++;digits++;}
            if(_at<text.Length&&text[_at]=='.'){_at++;while(_at<text.Length&&char.IsAsciiDigit(text[_at])){_at++;digits++;}}
            if(digits==0)throw Error("a finite number was expected");
            if(_at<text.Length&&text[_at] is 'e' or 'E')
            {
                _at++;if(_at<text.Length&&text[_at] is '+' or '-')_at++;
                var exponent=_at;while(_at<text.Length&&char.IsAsciiDigit(text[_at]))_at++;if(exponent==_at)throw Error("missing exponent digits");
            }
            if(!double.TryParse(text.AsSpan(start,_at-start),NumberStyles.Float,CultureInfo.InvariantCulture,out var value)||!double.IsFinite(value)||Math.Abs(value)>MaxCoordinate)throw Error("number out of range");
            return value;
        }
        private bool Flag(){Separator();if(_at>=text.Length||text[_at] is not ('0' or '1'))throw Error("arc flags must be 0 or 1");return text[_at++]=='1';}
        private DPoint Point(bool relative){var p=new DPoint(Number(),Number());return relative ? p+_current : p;}
        private void Flush()
        {
            if(_segments is not null){_figures.Add(new(_start,_segments.ToImmutable(),_closed));if(_figures.Count>MaxSegments)throw Error("too many figures");}
            _segments=null;_closed=false;
        }
        private void Add(VectorSegment segment,char command)
        {
            if(_segments is null||_closed)throw Error("a move command is required before drawing");
            if(++_count>MaxSegments)throw Error("too many segments");
            _segments.Add(segment);_current=segment.End;_lastCubic=segment.Control2;_lastQuadratic=segment.Control1;_previous=command;
        }
        public VectorPath Read()
        {
            White();var nonzero=false;
            if(_at<text.Length&&text[_at]=='F'){_at++;White();if(_at>=text.Length||text[_at] is not ('0' or '1'))throw Error("fill rule must be F0 or F1");nonzero=text[_at++]=='1';}
            char command='\0';
            while(true)
            {
                White();if(_at==text.Length)break;
                if(char.IsLetter(text[_at]))command=text[_at++];else if(command=='\0')throw Error("a command was expected");
                var upper=char.ToUpperInvariant(command);var relative=char.IsLower(command);
                switch(upper)
                {
                    case 'M':var point=Point(relative);Flush();_start=_current=point;_segments=ImmutableArray.CreateBuilder<VectorSegment>();_previous='M';command=relative ? 'l' : 'L';break;
                    case 'L':Add(VectorSegment.Line(Point(relative)),'L');break;
                    case 'H':var x=Number();Add(VectorSegment.Line(new(relative ? _current.X+x : x,_current.Y)),'H');break;
                    case 'V':var y=Number();Add(VectorSegment.Line(new(_current.X,relative ? _current.Y+y : y)),'V');break;
                    case 'C':var c1=Point(relative);var c2=Point(relative);Add(VectorSegment.Cubic(c1,c2,Point(relative)),'C');break;
                    case 'S':var first=_previous is 'C' or 'S' ? VectorMath.Lerp(_lastCubic,_current,2) : _current;var second=Point(relative);Add(VectorSegment.Cubic(first,second,Point(relative)),'S');break;
                    case 'Q':var control=Point(relative);Add(new(VectorSegmentKind.Quadratic,Point(relative)){Control1=control},'Q');break;
                    case 'T':var reflected=_previous is 'Q' or 'T' ? VectorMath.Lerp(_lastQuadratic,_current,2) : _current;Add(new(VectorSegmentKind.Quadratic,Point(relative)){Control1=reflected},'T');break;
                    case 'A':var rx=Number();var ry=Number();var angle=Number();var large=Flag();var sweep=Flag();Add(new(VectorSegmentKind.Arc,Point(relative)){Radius=new(rx,ry),Angle=angle,LargeArc=large,Clockwise=sweep},'A');break;
                    case 'Z':if(_segments is null||_closed)throw Error("unexpected close command");_closed=true;_current=_start;_previous='Z';command='\0';break;
                    default:throw Error("unsupported command "+command);
                }
                // An explicit separator must be followed by a number, never a command or end of input.
                White();if(_at<text.Length&&text[_at]==',')
                {
                    if(command=='\0')throw Error("unexpected comma");var next=_at+1;while(next<text.Length&&char.IsWhiteSpace(text[next]))next++;
                    if(next==text.Length||char.IsLetter(text[next]))throw Error("trailing comma");
                }
            }
            Flush();var result=new VectorPath(_figures.ToImmutable(),nonzero);Validate(result);return result;
        }
    }
}
