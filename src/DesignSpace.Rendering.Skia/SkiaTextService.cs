using System.Globalization;
using System.Text.RegularExpressions;
using DesignSpace.Core;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
namespace DesignSpace.Rendering.Skia;

/// <summary>Cached HarfBuzz glyph runs and grapheme-safe wrapping; not a full bidi paragraph engine.</summary>
public sealed class SkiaTextService : IDisposable
{
    private readonly record struct FontKey(string Family,float Size,bool Bold,bool Italic);
    private sealed record FontEntry(SKFont Font,SKShaper Shaper,SKTypeface? OwnedFace) : IDisposable
    {
        public void Dispose() { Shaper.Dispose(); Font.Dispose(); OwnedFace?.Dispose(); }
    }
    private sealed record Run(SKTextBlob? Blob,float Width,float Ascent,float Height);
    private readonly Dictionary<FontKey,FontEntry> _fonts=[];
    private readonly ResourceLruCache<(FontKey,string),Run> _runs=new(512,8*1024*1024,run=>run.Blob?.Dispose());
    private readonly ResourceLruCache<(FontKey,string,double),string[]> _lines=new(128,2*1024*1024);
    private readonly SKPaint _paint=new(){IsAntialias=true};
    public long LineLayoutCount { get; private set; }
    public long LineCacheHits { get; private set; }
    public long CachedRunBytes=>_runs.Cost;
    public long CachedLineBytes=>_lines.Cost;
    public long ShapeCount { get; private set; }
    public long CacheHits { get; private set; }
    private FontEntry Font(FontKey key)
    {
        if(_fonts.TryGetValue(key,out var entry)) return entry;
        if(_fonts.Count>=96) { ClearRuns(); foreach(var font in _fonts.Values) font.Dispose(); _fonts.Clear(); }
        var typeface=DesignTypography.DefaultTypeface; SKTypeface? owned=null;
        if(key.Family is not ("sans-serif" or "Segoe UI" or "Open Sans" or ""))
        {
            var candidate=SKTypeface.FromFamilyName(key.Family);
            if(candidate is not null && candidate.FamilyName.Equals(key.Family,StringComparison.OrdinalIgnoreCase)) typeface=owned=candidate;
            else candidate?.Dispose();
        }
        var fontInstance=new SKFont(typeface,key.Size) { Embolden=key.Bold,SkewX=key.Italic ? -.2f : 0 };
        _fonts[key]=entry=new(fontInstance,new SKShaper(typeface),owned); return entry;
    }
    private Run Shape(string text,FontKey key)
    {
        if(text.Length>32768) throw new InvalidOperationException("One text run is limited to 32,768 UTF-16 characters.");
        if(_runs.TryGetValue((key,text),out var run)) { CacheHits++; return run; }
        var font=Font(key); var result=font.Shaper.Shape(text,0,0,font.Font); ShapeCount++;
        SKTextBlob? blob=null;
        if(result.Codepoints.Length>0)
        {
            using var builder=new SKTextBlobBuilder(); var buffer=builder.AllocateRawPositionedRun(font.Font,result.Codepoints.Length,null);
            var glyphs=buffer.Glyphs; var positions=buffer.Positions;
            for(var i=0;i<result.Codepoints.Length;i++) { glyphs[i]=(ushort)result.Codepoints[i]; positions[i]=result.Points[i]; }
            blob=builder.Build();
        }
        var metrics=font.Font.Metrics; run=new(blob,result.Width,-metrics.Ascent,Math.Max(key.Size,metrics.Descent-metrics.Ascent+metrics.Leading));
        _runs.Add((key,text),run,128L+text.Length*2L+result.Codepoints.Length*16L); return run;
    }
    private static FontKey Key(double size,string family,bool bold=false,bool italic=false)=>new(family,(float)Math.Clamp(size,1,4096),bold,italic);
    private static FontKey NodeKey(DesignNode node)
    {
        var weight=node.Get("FontWeight");
        return Key(node.Number("FontSize",node.Type=="TextBlock" ? 14 : 13),node.Get("FontFamily","sans-serif"),
            weight is "Bold" or "SemiBold" or "Black" || Numbers.Parse(weight)>=600,node.Get("FontStyle")=="Italic");
    }
    private string[] Lines(string text,FontKey key,double width,bool wrap)
    {
        if(text.Length>32768) throw new InvalidOperationException("One text block is limited to 32,768 UTF-16 characters.");
        var constraint=wrap && double.IsFinite(width) && width>0 ? width : double.PositiveInfinity;
        var cacheKey=(key,text,constraint);
        if(_lines.TryGetValue(cacheKey,out var cached)) { LineCacheHits++;return cached; }
        LineLayoutCount++;
        var result=new List<string>();
        foreach(var paragraph in text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n').Split('\n'))
        {
            if(!wrap || !double.IsFinite(width) || width<=0 || Shape(paragraph,key).Width<=width) { result.Add(paragraph); continue; }
            var line=""; var lineWidth=0d;
            foreach(Match match in Regex.Matches(paragraph,@"\S+\s*|\s+"))
            {
                var token=match.Value; var tokenWidth=Shape(token,key).Width;
                if(line.Length>0 && lineWidth+tokenWidth>width) { result.Add(line.TrimEnd()); line=""; lineWidth=0; }
                if(tokenWidth<=width) { line+=token; lineWidth+=tokenWidth; continue; }
                var boundaries=StringInfo.ParseCombiningCharacters(token); var offset=0;
                while(offset<boundaries.Length)
                {
                    var low=offset+1; var high=boundaries.Length;
                    while(low<high)
                    {
                        var mid=(low+high+1)/2; var end=mid==boundaries.Length ? token.Length : boundaries[mid];
                        if(Shape(token[boundaries[offset]..end],key).Width<=width) low=mid; else high=mid-1;
                    }
                    var stop=low==boundaries.Length ? token.Length : boundaries[low]; var part=token[boundaries[offset]..stop]; offset=low;
                    if(offset<boundaries.Length) result.Add(part); else { line=part; lineWidth=Shape(part,key).Width; }
                }
            }
            result.Add(line.TrimEnd());
        }
        var lines=result.ToArray();
        _lines.Add(cacheKey,lines,128L+text.Length*2L+lines.Sum(line=>32L+line.Length*2L));
        return lines;
    }
    public DSize Measure(string text,double size,string family,double width=double.PositiveInfinity,bool wrap=false)
    {
        var key=Key(size,family); var lines=Lines(text,key,width,wrap); var height=Shape("Mg",key).Height;
        return new(lines.Select(line=>(double)Shape(line,key).Width).DefaultIfEmpty(0).Max(),Math.Max(1,lines.Length)*height);
    }
    public DSize Measure(DesignNode node,double width)
    {
        var key=NodeKey(node);var lines=Lines(node.Get("Text",node.Get("Content",node.TextContent)),key,width,node.Get("TextWrapping")=="Wrap");
        var naturalHeight=Shape("Mg",key).Height;var height=node.Number("LineHeight",naturalHeight);if(height<=0)height=naturalHeight;
        return new(lines.Select(line=>(double)Shape(line,key).Width).DefaultIfEmpty(0).Max(),Math.Max(1,lines.Length)*height);
    }
    public void Draw(SKCanvas canvas,string text,float x,float baseline,double size,SKColor color,string family="sans-serif")
    {
        var run=Shape(text,Key(size,family)); if(run.Blob is null) return;
        _paint.Color=color;canvas.DrawText(run.Blob,x,baseline,_paint);
    }
    public void DrawNode(SKCanvas canvas,DesignNode n,SKRect bounds,SKColor color,bool centered=false)
    {
        var size=n.Number("FontSize",n.Type=="TextBlock" ? 14 : 13); var weight=n.Get("FontWeight");
        var key=NodeKey(n);
        var text=n.Get("Text",n.Get("Content",n.TextContent)); var lines=Lines(text,key,bounds.Width,n.Get("TextWrapping")=="Wrap");
        var metrics=Shape("Mg",key); var lineHeight=n.Number("LineHeight",metrics.Height); if(lineHeight<=0) lineHeight=metrics.Height;
        var y=centered ? bounds.MidY-(float)(lines.Length*lineHeight)/2+metrics.Ascent : bounds.Top+metrics.Ascent;
        var paint=_paint;paint.Color=color;canvas.Save(); canvas.ClipRect(bounds);
        foreach(var line in lines)
        {
            var run=Shape(line,key); var alignment=n.Get("TextAlignment",centered ? "Center" : "Left");
            var x=alignment=="Center" ? bounds.MidX-run.Width/2 : alignment=="Right" ? bounds.Right-run.Width : bounds.Left;
            if(run.Blob is not null) canvas.DrawText(run.Blob,x,y,paint);
            if(n.Get("TextDecorations").Contains("Underline",StringComparison.Ordinal)) { paint.StrokeWidth=Math.Max(1,(float)size/16); canvas.DrawLine(x,y+2,x+run.Width,y+2,paint); }
            y+=(float)lineHeight; if(y>bounds.Bottom+lineHeight) break;
        }
        canvas.Restore();
    }
    private void ClearRuns()=>_runs.Clear();
    public void Dispose() { ClearRuns();_lines.Clear();_paint.Dispose(); foreach(var font in _fonts.Values) font.Dispose(); _fonts.Clear(); }
}
