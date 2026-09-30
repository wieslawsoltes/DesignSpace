using DesignSpace.Core;
namespace DesignSpace.Rendering.Skia;

public enum EffectRenderingMode { Native, WpfSoftwareCompatible }

/// <summary>Numerical conventions from WPF's MIT-licensed software effect implementation.
/// These do not implement every WPF pixel, transform or intermediate-surface rule.</summary>
public static class WpfEffectMath
{
    /// <summary>Fully covered, non-overlapping shadow alpha, including WPF's integer division by 65536.</summary>
    public static byte ShadowAlpha(double opacity)
    {
        if(!double.IsFinite(opacity)||opacity<0||opacity>1)throw new ArgumentOutOfRangeException(nameof(opacity));
        var amount=(int)(opacity*255);
        return (byte)(255*255*amount/65536);
    }
    /// <summary>Finite normalized Gaussian taps. WPF corrects float-rounding drift additively, not by dividing the sum.</summary>
    public static float[] GaussianKernel(int radius)
    {
        if(radius<0||radius>DesignEffect.MaxRadius)throw new ArgumentOutOfRangeException(nameof(radius));
        if(radius==0)return [1];
        var taps=new float[2*radius+1];var half=new float[radius+1];var sum=0d;var deviation=radius/3d;
        for(var i=0;i<=radius;i++)
        {
            half[i]=(float)(Math.Exp(-(double)i*i/(2*deviation*deviation))/(deviation*Math.Sqrt(2*Math.PI)));
            sum+=half[i];if(i>0)sum+=half[i];
        }
        var correction=(float)((1-sum)/(2*radius+1));
        for(var i=0;i<=radius;i++)taps[radius-i]=taps[radius+i]=half[i]+correction;
        return taps;
    }
}
