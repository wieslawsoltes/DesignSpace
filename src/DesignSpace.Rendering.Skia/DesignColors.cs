using System.Globalization;
using SkiaSharp;
namespace DesignSpace.Rendering.Skia;

/// <summary>XAML hexadecimal, named and scRGB color decoding.</summary>
public static class DesignColors
{
    public static SKColor Parse(string? text,SKColor fallback)
    {
        if(string.IsNullOrWhiteSpace(text))return fallback;text=text.Trim();
        if(text.Equals("Transparent",StringComparison.OrdinalIgnoreCase))return SKColors.Transparent;
        if(text.StartsWith('#'))
        {
            var value=text[1..];
            if(value.Length is 3 or 4)value=string.Concat(value.Select(c=>new string(c,2)));
            if(value.Length==6 && uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var rgb))return new((byte)(rgb>>16),(byte)(rgb>>8),(byte)rgb);
            if(value.Length==8 && uint.TryParse(value,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out var argb))return new((byte)(argb>>16),(byte)(argb>>8),(byte)argb,(byte)(argb>>24));
            return fallback;
        }
        if(text.StartsWith("sc#",StringComparison.OrdinalIgnoreCase))
        {
            var values=text[3..].Split(',',StringSplitOptions.TrimEntries);if(values.Length is not (3 or 4))return fallback;
            var numbers=new double[values.Length];
            for(var i=0;i<values.Length;i++)if(!double.TryParse(values[i],NumberStyles.Float,CultureInfo.InvariantCulture,out numbers[i])||!double.IsFinite(numbers[i]))return fallback;
            byte Srgb(double linear){linear=Math.Clamp(linear,0,1);return (byte)Math.Round(255*(linear<=.0031308 ? 12.92*linear : 1.055*Math.Pow(linear,1/2.4)-.055));}
            var offset=values.Length==4 ? 1 : 0;var alpha=values.Length==4 ? (byte)Math.Round(Math.Clamp(numbers[0],0,1)*255) : (byte)255;
            return new(Srgb(numbers[offset]),Srgb(numbers[offset+1]),Srgb(numbers[offset+2]),alpha);
        }
        var known=System.Drawing.Color.FromName(text);
        return known.IsKnownColor ? new SKColor(known.R,known.G,known.B,known.A) : fallback;
    }
}
