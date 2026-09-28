using System.Xml.Linq;
using DesignSpace.Core;
namespace DesignSpace.Engine;

internal sealed record GridTrack(string Unit,double Value,double Minimum=0,double Maximum=double.PositiveInfinity);
internal sealed record GridPlan(double[] Columns,double[] Rows,double ColumnSpacing,double RowSpacing);
internal static class GridTracks
{
    public static GridTrack[] Read(DesignNode node,bool rows)
    {
        var collection=rows ? "RowDefinitions" : "ColumnDefinitions"; var size=rows ? "Height" : "Width";
        GridTrack Track(string text,double min=0,double max=double.PositiveInfinity)
        {
            text=text.Trim(); return text=="Auto" ? new("Auto",0,min,max) : text.EndsWith('*') ? new("Star",text=="*" ? 1 : Math.Max(0,Numbers.Parse(text[..^1],1)),min,max) : new("Pixel",Math.Max(0,Numbers.Parse(text)),min,max);
        }
        var raw=node.PropertyElements.FirstOrDefault(s=>s.Contains("."+collection,StringComparison.Ordinal));
        if(raw is not null)
        {
            var tracks=XElement.Parse(raw).Elements().Select(e=>Track((string?)e.Attribute(size) ?? "*",Math.Max(0,Numbers.Parse((string?)e.Attribute("Min"+size))),Math.Max(0,Numbers.Parse((string?)e.Attribute("Max"+size),double.PositiveInfinity)))).ToArray();
            if(tracks.Length>512) throw new InvalidDataException("A grid is limited to 512 tracks per axis.");
            if(tracks.Length>0) return tracks;
        }
        var shorthand=node.Get(collection); return shorthand.Length==0 ? [new("Star",1)] : shorthand.Split(',').Take(512).Select(s=>Track(s)).ToArray();
    }
    public static double[] Initial(GridTrack[] tracks)=>tracks.Select(t=>Math.Clamp(t.Unit=="Pixel" ? t.Value : t.Minimum,t.Minimum,Math.Max(t.Minimum,t.Maximum))).ToArray();
    public static int Index(DesignNode n,string key,int count)=>Math.Clamp((int)n.Number(key),0,count-1);
    public static int Span(DesignNode n,string key,int count,int start)=>Math.Clamp((int)n.Number(key,1),1,count-start);
    public static double Sum(double[] sizes,int start,int span,double spacing)=>sizes.Skip(start).Take(span).Sum()+Math.Max(0,span-1)*spacing;
    public static void Demand(GridTrack[] tracks,double[] sizes,int start,int span,double required,double spacing,bool allowStars)
    {
        var deficit=required-Sum(sizes,start,span,spacing); if(deficit<=0 || !double.IsFinite(deficit)) return;
        var candidates=Enumerable.Range(start,span).Where(i=>tracks[i].Unit=="Auto").ToArray();
        if(candidates.Length==0 && allowStars) candidates=Enumerable.Range(start,span).Where(i=>tracks[i].Unit=="Star").ToArray();
        for(var pass=0;pass<candidates.Length && deficit>1e-7;pass++)
        {
            var active=candidates.Where(i=>sizes[i]<tracks[i].Maximum).ToArray(); if(active.Length==0) break;
            var share=deficit/active.Length;
            foreach(var i in active) { var addition=Math.Min(share,tracks[i].Maximum-sizes[i]); sizes[i]+=addition; deficit-=addition; }
        }
    }
    public static void Resolve(GridTrack[] tracks,double[] sizes,double available,double spacing)
    {
        if(!double.IsFinite(available)) return;
        var stars=Enumerable.Range(0,tracks.Length).Where(i=>tracks[i].Unit=="Star").ToArray(); if(stars.Length==0) return;
        var remaining=Math.Max(0,available-Math.Max(0,tracks.Length-1)*spacing-Enumerable.Range(0,tracks.Length).Where(i=>tracks[i].Unit!="Star").Sum(i=>sizes[i]));
        var low=0d; var high=remaining/Math.Max(1e-9,stars.Where(i=>tracks[i].Value>0).Select(i=>tracks[i].Value).DefaultIfEmpty(1).Min())+stars.Max(i=>tracks[i].Minimum)+1;
        for(var pass=0;pass<48;pass++)
        {
            var mid=(low+high)/2; var total=stars.Sum(i=>Math.Clamp(mid*tracks[i].Value,tracks[i].Minimum,Math.Max(tracks[i].Minimum,tracks[i].Maximum)));
            if(total<remaining) low=mid; else high=mid;
        }
        foreach(var i in stars) sizes[i]=Math.Clamp((low+high)/2*tracks[i].Value,tracks[i].Minimum,Math.Max(tracks[i].Minimum,tracks[i].Maximum));
    }
}
