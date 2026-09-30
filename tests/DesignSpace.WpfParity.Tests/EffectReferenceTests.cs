using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Rendering.Skia;
using SkiaSharp;

internal static class EffectReferenceTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;var samples=0;var maxInteriorDifference=0;
        var rows=new List<object>();var profiles=new List<object>();
        const int tolerance=1; // one 8-bit channel level, for opacity quantization at fully covered pixels
        const string directory="artifacts/verification/wpf-effects";
        Directory.CreateDirectory(directory);
        byte[] Native(DesignEffect? effect,double opacity,int scale,out byte[] png)
        {
            var root=new Canvas{Width=300,Height=220,Background=Brushes.White};
            var rectangle=new Rectangle{Width=40,Height=40,Fill=Brushes.Red,Opacity=opacity};
            Canvas.SetLeft(rectangle,70);Canvas.SetTop(rectangle,70);root.Children.Add(rectangle);
            if(effect is { } e)rectangle.Effect=e.Kind==DesignEffectKind.Blur?
                new BlurEffect{Radius=e.Radius,KernelType=e.Kernel==DesignBlurKernel.Box?KernelType.Box:KernelType.Gaussian,RenderingBias=RenderingBias.Quality}:
                new DropShadowEffect{BlurRadius=e.Radius,Direction=e.Direction,ShadowDepth=e.ShadowDepth,Opacity=e.Opacity,Color=(Color)ColorConverter.ConvertFromString(e.Color),RenderingBias=RenderingBias.Quality};
            root.Measure(new Size(300,220));root.Arrange(new Rect(0,0,300,220));root.UpdateLayout();
            var bitmap=new RenderTargetBitmap(300*scale,220*scale,96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(root);
            var data=new byte[300*220*4*scale*scale];bitmap.CopyPixels(data,300*scale*4,0);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();encoder.Save(output);png=output.ToArray();return data;
        }
        byte[] Portable(DesignEffect? effect,double opacity,int scale)
        {
            var n=DesignNode.Create("Rectangle","Target",70,70,40,40).Set("Fill","Red").Set("Opacity",opacity);
            var d=new DesignDocument{Root=DesignNode.Create("Canvas","Root",0,0,300,220).Set("Background","White") with{Children=[n]}};
            if(effect is not null)d=EffectEditing.Apply(d,[n.Id],effect);
            using var r=new DesignRenderer();return r.ExportPng(new LayoutEngine(r).Arrange(d.Root),scale);
        }
        foreach(var angle in new[]{0d,45,90,135,180,225,270,315})foreach(var opacity in new[]{0d,.5,1})foreach(var sourceOpacity in new[]{.5,1})foreach(var scale in new[]{1,2})
        {
            var name=$"hard shadow {angle}, opacity {opacity}, source {sourceOpacity}, scale {scale}";
            try
            {
                var effect=new DesignEffect{Kind=DesignEffectKind.DropShadow,Radius=0,Direction=angle,ShadowDepth=60,Opacity=opacity,Color="#000000FF"};
                var native=Native(effect,sourceOpacity,scale,out var nativePng);var portable=Portable(effect,sourceOpacity,scale);using var image=SKBitmap.Decode(portable);
                var delta=effect.ShadowOffset;
                var points=new[]{new DPoint(90,90),new DPoint(90+delta.X,90+delta.Y),new DPoint(250,190)};
                var difference=0;
                foreach(var point in points)
                {
                    var px=(int)Math.Round(point.X*scale);var py=(int)Math.Round(point.Y*scale);var at=(py*300*scale+px)*4;var value=image.GetPixel(px,py);
                    foreach(var (expected,actual) in new[]{(native[at],value.Blue),(native[at+1],value.Green),(native[at+2],value.Red),(native[at+3],value.Alpha)})
                    {difference=Math.Max(difference,Math.Abs(expected-actual));samples++;}
                }
                maxInteriorDifference=Math.Max(maxInteriorDifference,difference);
                if(difference>tolerance)throw new InvalidOperationException($"Interior channel error {difference} exceeds {tolerance}.");
                if(angle==315&&opacity==1&&sourceOpacity==1&&scale==1){File.WriteAllBytes(directory+"/hard-shadow-native.png",nativePng);File.WriteAllBytes(directory+"/hard-shadow-designspace.png",portable);}
                passed++;rows.Add(new{name,passed=true,difference});
            }
            catch(Exception e){failed++;rows.Add(new{name,passed=false,error=e.ToString()});Console.Error.WriteLine("FAIL WPF effects: "+name+": "+e);}
        }
        // Soft-edge profiles are measurements, not equivalence passes. Preserve both
        // native images and exact samples so differences cannot be hidden by a tolerance.
        foreach(var kernel in Enum.GetValues<DesignBlurKernel>())foreach(var radius in new[]{3d,9,18})
        {
            var effect=new DesignEffect{Radius=radius,Kernel=kernel,RenderingBias="Quality"};var name=$"{kernel}-{radius}";
            try
            {
                var native=Native(effect,1,1,out var nativePng);var portable=Portable(effect,1,1);using var image=SKBitmap.Decode(portable);
                var profile=new List<object>();var maximum=0;
                for(var x=45;x<=100;x++){var reference=native[(90*300+x)*4+1];var value=image.GetPixel(x,90).Green;maximum=Math.Max(maximum,Math.Abs(reference-value));profile.Add(new{x,native=reference,designSpace=value});}
                profiles.Add(new{name,maximum,profile});File.WriteAllBytes(directory+"/"+name+"-native.png",nativePng);File.WriteAllBytes(directory+"/"+name+"-designspace.png",portable);
            }
            catch(Exception e){failed++;Console.Error.WriteLine("FAIL WPF effect measurement: "+name+": "+e);profiles.Add(new{name,error=e.ToString()});}
        }
        File.WriteAllText("artifacts/verification/wpf-effect-results.json",JsonSerializer.Serialize(new{passed,failed,samples,maxInteriorDifference,tolerance,
            scope="Native WPF hard-shadow interior pixels, directions, alpha and scale. Soft blur profiles are measurements, not pixel-equivalence passes. No Blend UI or hardware-GPU qualification.",rows,profiles},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"WPF effects: {passed} semantic cases passed, {failed} failed; {samples} interior channels; maximum difference {maxInteriorDifference}.");return(passed,failed);
    }
}
