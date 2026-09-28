using System.Globalization;
namespace DesignSpace.Core;

/// <summary>Unpremultiplied sRGB color channels used by portable gradient editing.</summary>
public readonly record struct BrushColor(double R,double G,double B,double A=1)
{
    public static BrushColor Parse(string text)
    {
        if(string.IsNullOrWhiteSpace(text))throw new InvalidDataException("A literal brush color is required.");text=text.Trim();
        if(text.StartsWith('#'))
        {
            var hex=text[1..];if(hex.Length is 3 or 4)hex=string.Concat(hex.Select(c=>new string(c,2)));
            if(hex.Length is not (6 or 8)||!uint.TryParse(hex,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var value))throw new InvalidDataException("Invalid hexadecimal brush color.");
            return new(((value>>16)&255)/255d,((value>>8)&255)/255d,(value&255)/255d,hex.Length==8?(value>>24)/255d:1);
        }
        if(text.StartsWith("sc#",StringComparison.OrdinalIgnoreCase))
        {
            var values=text[3..].Split(',').Select(BrushCodec.ReadNumber).ToArray();if(values.Length is not (3 or 4))throw new InvalidDataException("scRGB colors need three or four channels.");
            double Gamma(double x){x=Math.Clamp(x,0,1);return x<=.0031308?12.92*x:1.055*Math.Pow(x,1/2.4)-.055;}
            var at=values.Length==4?1:0;return new(Gamma(values[at]),Gamma(values[at+1]),Gamma(values[at+2]),at==1?Math.Clamp(values[0],0,1):1);
        }
        var color=System.Drawing.Color.FromName(text);if(!color.IsKnownColor)throw new InvalidDataException("Unknown literal brush color: "+text);
        return new(color.R/255d,color.G/255d,color.B/255d,color.A/255d);
    }
    public static BrushColor Lerp(BrushColor a,BrushColor b,double t)=>new(a.R+(b.R-a.R)*t,a.G+(b.G-a.G)*t,a.B+(b.B-a.B)*t,a.A+(b.A-a.A)*t);
    public string ToHex()
    {
        byte Byte(double value)=>(byte)Math.Round(Math.Clamp(value,0,1)*255);
        return $"#{Byte(A):X2}{Byte(R):X2}{Byte(G):X2}{Byte(B):X2}";
    }
}
