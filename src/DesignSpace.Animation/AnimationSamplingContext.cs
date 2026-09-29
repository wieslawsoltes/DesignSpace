using System.Runtime.CompilerServices;
using DesignSpace.Core;
namespace DesignSpace.Animation;

/// <summary>Exact sampling is deterministic. WpfCompatible reproduces WPF's history-dependent KeySpline approximation.</summary>
public enum SplineSamplingMode { Exact, WpfCompatible }

/// <summary>Per-playback spline state, separate from immutable documents. Use one context per playback and UI thread.</summary>
public sealed class AnimationSamplingContext
{
    private readonly ConditionalWeakTable<AnimationKey,WpfKeySplineSampler> _samplers=new();
    public SplineSamplingMode Mode { get; }
    public AnimationSamplingContext(SplineSamplingMode mode=SplineSamplingMode.Exact)
    {
        if(!Enum.IsDefined(mode))throw new ArgumentOutOfRangeException(nameof(mode));
        Mode=mode;
    }
    public double Sample(AnimationKey key,double progress)
    {
        ArgumentNullException.ThrowIfNull(key);
        var spline=key.Spline??throw new InvalidDataException("A spline key requires control points.");
        return Mode==SplineSamplingMode.Exact ? spline.Evaluate(progress)
            : _samplers.GetValue(key,k=>new WpfKeySplineSampler(k.Spline!)).Evaluate(progress);
    }
    /// <summary>Reset on a new playback. A seek within the same playback intentionally retains WPF's previous guess.</summary>
    public void Reset()=>_samplers.Clear();
}
