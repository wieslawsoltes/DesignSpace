using System.Xml.Linq;
namespace DesignSpace.Core;

/// <summary>Double precision affine transform. A*B applies B first, then A.</summary>
public readonly record struct DMatrix(double M11,double M12,double M21,double M22,double DX,double DY)
{
    public static DMatrix Identity=>new(1,0,0,1,0,0);
    public DPoint Map(DPoint p)=>new(M11*p.X+M21*p.Y+DX,M12*p.X+M22*p.Y+DY);
    public DPoint MapVector(DPoint p)=>new(M11*p.X+M21*p.Y,M12*p.X+M22*p.Y);
    public static DMatrix operator *(DMatrix a,DMatrix b)=>new(a.M11*b.M11+a.M21*b.M12,a.M12*b.M11+a.M22*b.M12,a.M11*b.M21+a.M21*b.M22,a.M12*b.M21+a.M22*b.M22,a.M11*b.DX+a.M21*b.DY+a.DX,a.M12*b.DX+a.M22*b.DY+a.DY);
    public static DMatrix Translate(double x,double y)=>new(1,0,0,1,x,y);
    public static DMatrix Scale(double x,double y)=>new(x,0,0,y,0,0);
    public static DMatrix Rotate(double degrees,double cx=0,double cy=0)
    {
        var r=degrees*Math.PI/180; var c=Math.Cos(r); var s=Math.Sin(r);
        return Translate(cx,cy)*new DMatrix(c,s,-s,c,0,0)*Translate(-cx,-cy);
    }
    public bool TryInvert(out DMatrix inverse)
    {
        var det=M11*M22-M12*M21;
        if(!double.IsFinite(det) || Math.Abs(det)<1e-12) { inverse=Identity; return false; }
        inverse=new(M22/det,-M12/det,-M21/det,M11/det,(M21*DY-M22*DX)/det,(M12*DX-M11*DY)/det); return true;
    }
    public DRect MapBounds(DRect b)
    {
        var a=Map(new(b.X,b.Y)); var c=Map(new(b.Right,b.Y)); var d=Map(new(b.Right,b.Bottom)); var e=Map(new(b.X,b.Bottom));
        var left=Math.Min(Math.Min(a.X,c.X),Math.Min(d.X,e.X)); var top=Math.Min(Math.Min(a.Y,c.Y),Math.Min(d.Y,e.Y));
        return new(left,top,Math.Max(Math.Max(a.X,c.X),Math.Max(d.X,e.X))-left,Math.Max(Math.Max(a.Y,c.Y),Math.Max(d.Y,e.Y))-top);
    }
}
public static class DesignTransforms
{
    public static DMatrix Read(DesignNode n,DRect bounds)
    {
        var matrix=DMatrix.Identity;
        var raw=n.PropertyElements.FirstOrDefault(s=>s.Contains(".RenderTransform",StringComparison.Ordinal));
        if(raw is not null) matrix=ReadElement(XElement.Parse(raw).Elements().FirstOrDefault());
        else if(n.Get("RenderTransform").Length>0 && !n.Get("RenderTransform").StartsWith('{')) matrix=ParseMatrix(n.Get("RenderTransform"));
        var origin=n.Get("RenderTransformOrigin","0,0").Split(',');
        var x=bounds.X+(origin.Length==2 ? Numbers.Parse(origin[0])*bounds.Width : 0);
        var y=bounds.Y+(origin.Length==2 ? Numbers.Parse(origin[1])*bounds.Height : 0);
        return DMatrix.Rotate(n.Rotation,bounds.Center.X,bounds.Center.Y)*DMatrix.Translate(x,y)*matrix*DMatrix.Translate(-x,-y);
    }
    private static DMatrix ParseMatrix(string value)
    {
        var parts=value.Split([',',' '],StringSplitOptions.RemoveEmptyEntries);
        return parts.Length==6 ? new(Numbers.Parse(parts[0],1),Numbers.Parse(parts[1]),Numbers.Parse(parts[2]),Numbers.Parse(parts[3],1),Numbers.Parse(parts[4]),Numbers.Parse(parts[5])) : DMatrix.Identity;
    }
    private static DMatrix ReadElement(XElement? e)
    {
        if(e is null) return DMatrix.Identity;
        double N(string p,double fallback=0)=>Numbers.Parse((string?)e.Attribute(p),fallback);
        var center=DMatrix.Translate(N("CenterX"),N("CenterY")); var uncenter=DMatrix.Translate(-N("CenterX"),-N("CenterY"));
        DMatrix Skew()=>new(1,Math.Tan(N("AngleY")*Math.PI/180),Math.Tan(N("AngleX")*Math.PI/180),1,0,0);
        switch(e.Name.LocalName)
        {
            case "TranslateTransform": return DMatrix.Translate(N("X"),N("Y"));
            case "ScaleTransform": return center*DMatrix.Scale(N("ScaleX",1),N("ScaleY",1))*uncenter;
            case "RotateTransform": return DMatrix.Rotate(N("Angle"),N("CenterX"),N("CenterY"));
            case "SkewTransform": return center*Skew()*uncenter;
            case "MatrixTransform": return ParseMatrix((string?)e.Attribute("Matrix") ?? "");
            case "CompositeTransform":
                var skew=new DMatrix(1,Math.Tan(N("SkewY")*Math.PI/180),Math.Tan(N("SkewX")*Math.PI/180),1,0,0);
                return DMatrix.Translate(N("TranslateX"),N("TranslateY"))*center*DMatrix.Rotate(N("Rotation"))*skew*DMatrix.Scale(N("ScaleX",1),N("ScaleY",1))*uncenter;
            case "TransformGroup":
                var result=DMatrix.Identity;
                foreach(var child in e.Elements())
                {
                    if(child.Name.LocalName=="TransformGroup.Children") foreach(var item in child.Elements()) result=ReadElement(item)*result;
                    else result=ReadElement(child)*result;
                }
                return result;
            default: return DMatrix.Identity;
        }
    }
}
