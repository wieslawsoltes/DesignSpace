using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Xaml;
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var passed=0;var failed=0;var results=new List<object>();
        const string ns=DesignNode.PresentationNamespace,x=DesignNode.XamlNamespace;
        string ColorText(string? value)
        {
            if(value is null||value=="{x:Null}"||value=="")return "null";
            if(value.StartsWith('<'))value=(string?)XElement.Parse(value).Attribute("Color")??"Transparent";
            return ((Color)ColorConverter.ConvertFromString(value)).ToString();
        }
        foreach(var parentEnabled in new[]{false,true})foreach(var active in new[]{false,true})foreach(var local in new[]{false,true})foreach(var styleTrigger in new[]{false,true})foreach(var named in new[]{false,true})foreach(var multi in new[]{false,true})
        {
            var name=$"parentEnabled={parentEnabled}, active={active}, local={local}, style={styleTrigger}, named={named}, multi={multi}";
            try
            {
                var setter=$"<Setter {(named?"TargetName='Chrome'":"")} Property='Background' Value='Red'/>";
                var rule=multi?$"<MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='Active'/><Condition Property='IsEnabled' Value='True'/></MultiTrigger.Conditions>{setter}</MultiTrigger>":$"<Trigger Property='Tag' Value='Active'>{setter}</Trigger>";
                var style=styleTrigger?"<Style.Triggers><MultiTrigger><MultiTrigger.Conditions><Condition Property='Tag' Value='Active'/><Condition Property='IsEnabled' Value='True'/></MultiTrigger.Conditions><Setter Property='Background' Value='Yellow'/></MultiTrigger></Style.Triggers>":"";
                var xml=$"<Canvas xmlns='{ns}' xmlns:x='{x}' Width='400' Height='300' IsEnabled='{parentEnabled}'><Canvas.Resources><ControlTemplate x:Key='T' TargetType='Button'><Border x:Name='Chrome' Background='{{TemplateBinding Background}}'/><ControlTemplate.Triggers>{rule}</ControlTemplate.Triggers></ControlTemplate><Style TargetType='Button'><Setter Property='IsEnabled' Value='True'/><Setter Property='Background' Value='Blue'/>{style}</Style></Canvas.Resources><Button x:Name='Target' Width='120' Height='60' Template='{{StaticResource T}}' Tag='{(active?"Active":"Rest")}' {(local?"Background='Lime'":"")}/></Canvas>";
                var native=(Canvas)XamlReader.Parse(xml);var button=(Button)native.Children[0];
                native.Measure(new Size(400,300));native.Arrange(new Rect(0,0,400,300));native.UpdateLayout();button.ApplyTemplate();
                var border=(Border)button.Template.FindName("Chrome",button);var expected=((SolidColorBrush)border.Background).Color.ToString();
                var document=XamlCodec.Parse(xml).Document;var preview=DesignPreview.Get(document.Root);
                if(preview.Diagnostics.Count!=0)throw new InvalidDataException(string.Join(";",preview.Diagnostics));
                var part=preview.Root.DescendantsAndSelf().Single(n=>n.Name=="Target_Chrome");var actual=ColorText(new BrushResolver(preview.Root).Resolve(part.Id,"Background"));
                if(expected!=actual)throw new Exception($"WPF={expected}, DesignSpace={actual}");
                // Returning to an inactive condition must restore the underlying value.
                button.Tag="Rest";native.UpdateLayout();var reset=((SolidColorBrush)border.Background).Color.ToString();
                var resetRoot=document.Root.Update(document.Root.Children[0].Id,n=>n.Set("Tag","Rest"));var restored=DesignPreview.Resolve(resetRoot);
                var resetPart=restored.DescendantsAndSelf().Single(n=>n.Name=="Target_Chrome");var restoredColor=ColorText(new BrushResolver(restored).Resolve(resetPart.Id,"Background"));
                if(reset!=restoredColor)throw new Exception($"Reset WPF={reset}, DesignSpace={restoredColor}");
                passed++;results.Add(new{name,passed=true,expected,actual,reset});
            }
            catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.ToString()});Console.Error.WriteLine("FAIL WPF template: "+name+": "+e);}
        }
        Directory.CreateDirectory("artifacts/verification");
        File.WriteAllText("artifacts/verification/wpf-template-results.json",JsonSerializer.Serialize(new{passed,failed,scope="Native WPF effective brush values for local/style/template and named-part Trigger/MultiTrigger precedence, inherited disabled coercion and reset. Not Blend UI pixels or complete dependency-property semantics.",results},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"WPF templates: {passed} passed, {failed} failed.");return failed==0?0:1;
    }
}
