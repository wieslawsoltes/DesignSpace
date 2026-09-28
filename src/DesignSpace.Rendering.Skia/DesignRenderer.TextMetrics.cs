using DesignSpace.Core;
using DesignSpace.Engine;
namespace DesignSpace.Rendering.Skia;

public sealed partial class DesignRenderer : IStyledTextMetrics
{
    public long TextLayoutCount=>_text.LineLayoutCount;
    public long TextLayoutCacheHits=>_text.LineCacheHits;
    public DSize Measure(DesignNode node,double width)=>_text.Measure(node,width);
}
