// WPF easing mathematics adapted from dotnet/wpf, licensed by the .NET Foundation
// under MIT. See THIRD-PARTY-NOTICES.md for source attribution and the full license.
using System.Text.Json.Serialization;
namespace DesignSpace.Core;

public enum EasingFamily { Back, Bounce, Circle, Cubic, Elastic, Exponential, Power, Quadratic, Quartic, Quintic, Sine }
public enum EasingDirection { EaseIn, EaseOut, EaseInOut }

/// <summary>Immutable built-in easing parameters. Normalized outputs may overshoot; they are never clamped.</summary>
public sealed record EasingCurve(EasingFamily Family, EasingDirection Mode=EasingDirection.EaseOut,
    double Amplitude=1, int Bounces=3, double Bounciness=2, int Oscillations=3,
    double Springiness=3, double Exponent=2, double Power=2)
{
    public void Validate()
    {
        if(!Enum.IsDefined(Family)||!Enum.IsDefined(Mode))throw new InvalidDataException("Unknown easing family or direction.");
        if(!double.IsFinite(Amplitude)||Math.Abs(Amplitude)>1000||!double.IsFinite(Bounciness)||Math.Abs(Bounciness)>1000||
           !double.IsFinite(Springiness)||Math.Abs(Springiness)>100||!double.IsFinite(Exponent)||Math.Abs(Exponent)>100||
           !double.IsFinite(Power)||Math.Abs(Power)>1000||Bounces < -32||Bounces>32||Oscillations < -64||Oscillations>64)
            throw new InvalidDataException("Easing parameters exceed the finite preview budget: amplitude/power/bounciness ±1000, exponent/springiness ±100, bounces ±32 and oscillations ±64.");
    }
    /// <summary>Samples a normalized time without allocating. Negative parameter coercion follows WPF.</summary>
    public double Evaluate(double progress)
    {
        Validate();
        if(!double.IsFinite(progress)||progress<0||progress>1)throw new ArgumentOutOfRangeException(nameof(progress));
        return Mode switch
        {
            EasingDirection.EaseIn=>In(progress),
            EasingDirection.EaseOut=>1-In(1-progress),
            _=>progress<.5 ? In(progress*2)*.5 : (1-In((1-progress)*2))*.5+.5
        };
    }
    private static double Exponential(double t,double exponent)=>Math.Abs(exponent)<2.2204460492503131e-15
        ? t : (Math.Exp(exponent*t)-1)/(Math.Exp(exponent)-1);
    private double In(double t)=>Family switch
    {
        EasingFamily.Back=>Math.Pow(t,3)-t*Math.Max(0,Amplitude)*Math.Sin(Math.PI*t),
        EasingFamily.Bounce=>Bounce(t),
        EasingFamily.Circle=>1-Math.Sqrt(1-t*t),
        EasingFamily.Cubic=>t*t*t,
        EasingFamily.Elastic=>Exponential(t,Math.Max(0,Springiness))*Math.Sin((Math.PI*2*Math.Max(0,Oscillations)+Math.PI*.5)*t),
        EasingFamily.Exponential=>Exponential(t,Exponent),
        EasingFamily.Power=>Math.Pow(t,Math.Max(0,Power)),
        EasingFamily.Quadratic=>t*t,
        EasingFamily.Quartic=>t*t*t*t,
        EasingFamily.Quintic=>t*t*t*t*t,
        EasingFamily.Sine=>1-Math.Sin((1-t)*Math.PI*.5),
        _=>throw new InvalidOperationException("Unknown easing family.")
    };
    private double Bounce(double t)
    {
        var count=Math.Max(0,Bounces);var ratio=Bounciness;
        if(ratio<1||Math.Abs(ratio-1)<2.2204460492503131e-15)ratio=1.001;
        var power=Math.Pow(ratio,count);var complement=1-ratio;
        var units=(1-power)/complement+power*.5;
        var bounce=Math.Floor(Math.Log(-t*units*(1-ratio)+1,ratio));
        var start=(1-Math.Pow(ratio,bounce))/(complement*units);
        var end=(1-Math.Pow(ratio,bounce+1))/(complement*units);
        var middle=(start+end)*.5;var offset=t-middle;var radius=middle-start;
        var amplitude=Math.Pow(1/ratio,count-bounce);
        return (-amplitude/(radius*radius))*(offset-radius)*(offset+radius);
    }
}

public sealed partial record AnimationKey
{
    [JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)]
    public EasingCurve? Function { get; init; }
}
