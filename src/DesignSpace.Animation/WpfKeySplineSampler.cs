// Numerical algorithm adapted from dotnet/wpf KeySpline.cs (MIT).
// Copyright (c) .NET Foundation and Contributors. See THIRD-PARTY-NOTICES.md.
using DesignSpace.Core;
namespace DesignSpace.Animation;

/// <summary>
/// Stateful WPF KeySpline numerical compatibility. Unlike KeySpline.Evaluate, output can depend
/// on the preceding samples. The immutable curve and document never contain this playback state.
/// </summary>
public sealed class WpfKeySplineSampler
{
    private const double Accuracy=.001,Fuzz=.000001;
    private readonly double _bx,_cx,_dx,_endDx,_by,_cy;
    private readonly bool _identity;
    private double _parameter;
    public WpfKeySplineSampler(KeySpline curve)
    {
        ArgumentNullException.ThrowIfNull(curve);curve.Validate();
        _identity=curve==KeySpline.Linear;
        _bx=3*curve.X1;_cx=3*curve.X2;_dx=2*(_cx-_bx);_endDx=3-_cx;
        _by=3*curve.Y1;_cy=3*curve.Y2;
    }
    public void Reset()=>_parameter=0;
    public double Evaluate(double progress)
    {
        if(!double.IsFinite(progress))throw new ArgumentOutOfRangeException(nameof(progress));
        progress=Math.Clamp(progress,0,1);
        if(_identity)return progress;
        if(progress==0)_parameter=0;
        else if(progress==1)_parameter=1;
        else
        {
            var bottom=0d;var top=1d;
            // Same coefficients, stopping rule and previous-parameter seed as native WPF.
            // Do not replace this with a fixed absolute y tolerance: stationary tangents invalidate that bound.
            while(top-bottom>Fuzz)
            {
                var t=_parameter;var s=1-t;var t2=t*t;var s2=s*s;
                var x=_bx*t*s2+_cx*t2*s+t2*t;
                var derivative=_bx*s2+_dx*s*t+_endDx*t2;
                var magnitude=Math.Abs(derivative);
                if(x>progress)top=t;else bottom=t;
                if(Math.Abs(x-progress)<Accuracy*magnitude)break;
                if(magnitude>Fuzz)
                {
                    var next=t-(x-progress)/derivative;
                    _parameter=next>=top ? (t+top)/2 : next<=bottom ? (t+bottom)/2 : next;
                }
                else _parameter=(bottom+top)/2;
            }
        }
        var u=1-_parameter;var square=_parameter*_parameter;
        return _by*_parameter*u*u+_cy*square*u+square*_parameter;
    }
}
