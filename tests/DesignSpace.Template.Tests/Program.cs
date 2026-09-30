using System.Collections.Immutable;
using System.Text.Json;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;
using DesignSpace.Rendering.Skia;
using SkiaSharp;

var passed=0;var failed=0;long warmBytes=0;
void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS template: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL template: "+name+": "+e);}}
void Check(bool value,string message="Assertion failed"){if(!value)throw new Exception(message);}
void Reject(Action run){try{run();}catch(InvalidDataException){return;}throw new Exception("Expected InvalidDataException");}
const string ns=DesignNode.PresentationNamespace,x=DesignNode.XamlNamespace;
const string hover="<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Background' Value='Red'/></Trigger>";
DesignDocument Fixture(string triggers=hover,string style="",string local="",string visual="",string resources="")=>XamlCodec.Parse($"""
<Canvas xmlns="{ns}" xmlns:x="{x}" Width="400" Height="300" Background="White">
 <Canvas.Resources>{resources}<ControlTemplate x:Key="Template" TargetType="Button">
 {(visual.Length==0?"<Border x:Name='Chrome' Background='{TemplateBinding Background}'><ContentPresenter x:Name='Label'/></Border>":visual)}
 <ControlTemplate.Triggers>{triggers}</ControlTemplate.Triggers></ControlTemplate>{style}</Canvas.Resources>
 <Button x:Name="Target" Canvas.Left="40" Canvas.Top="60" Width="120" Height="60" Template="{{StaticResource Template}}" Content="Test" {local}/>
</Canvas>
""").Document;
DesignNode Target(DesignNode root)=>root.Children[0];
DesignNode Part(DesignNode root,string name="Chrome")=>root.DescendantsAndSelf().Single(n=>n.Name=="Target_"+name);
DesignNode Preview(DesignDocument document,bool over=false,bool pressed=false,bool focus=false)
{
    var id=document.Root.Children[0].Id;var context=new DesignPreviewContext();context.Update(new(){Hovered=over?id:null,Pressed=pressed?id:null,Focused=focus?id:null});
    var result=context.Resolve(document.Root);Check(context.Diagnostics.Count==0,string.Join("; ",context.Diagnostics));return result;
}
string? Brush(DesignNode root,DesignNode node,string property="Background")=>new BrushResolver(root).Resolve(node.Id,property);
Test("hover trigger is transient and leaves source identity unchanged",()=>{var d=Fixture(local:"Background='Blue'");var before=XamlCodec.Write(d);var normal=Preview(d);var over=Preview(d,true);Check(Brush(normal,Part(normal))=="Blue");Check(Brush(over,Part(over))=="Red");Check(before==XamlCodec.Write(d)&&d.Root.Children[0].Children.Length==0);});
Test("part identifiers remain stable across interaction states",()=>{var d=Fixture();Check(Part(Preview(d)).Id==Part(Preview(d,true)).Id);});
Test("MultiTrigger requires every Boolean condition",()=>
{
    var d=Fixture("<MultiTrigger><MultiTrigger.Conditions><Condition Property='IsMouseOver' Value='True'/><Condition Property='IsPressed' Value='True'/></MultiTrigger.Conditions><Setter TargetName='Chrome' Property='Background' Value='Lime'/></MultiTrigger>",local:"Background='Blue'");
    foreach(var over in new[]{false,true})foreach(var down in new[]{false,true}){var p=Preview(d,over,down);Check(Brush(p,Part(p))==(over&&down?"Lime":"Blue"));}
});
Test("last matching trigger setter wins",()=>{var d=Fixture(hover+"<Trigger Property='IsMouseOver' Value='True'><Trigger.Setters><Setter TargetName='Chrome' Property='Background' Value='Lime'/></Trigger.Setters></Trigger>");var p=Preview(d,true);Check(Brush(p,Part(p))=="Lime");});
Test("template self trigger updates TemplateBinding",()=>{var d=Fixture("<Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Red'/></Trigger>");var p=Preview(d,true);Check(Target(p).Get("Background")=="Red"&&Brush(p,Part(p))=="Red");});
Test("local owner value outranks template self trigger",()=>{var d=Fixture("<Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Red'/></Trigger>",local:"Background='Blue'");var p=Preview(d,true);Check(Brush(p,Part(p))=="Blue");});
Test("style trigger outranks style setter",()=>{var d=Fixture("",style:"<Style TargetType='Button'><Setter Property='Background' Value='Blue'/><Style.Triggers><Trigger Property='IsMouseOver' Value='true'><Setter Property='Background' Value='Red'/></Trigger></Style.Triggers></Style>");var p=Preview(d,true);Check(Brush(p,Part(p))=="Red");});
Test("local owner value outranks style trigger",()=>{var d=Fixture("",style:"<Style TargetType='Button'><Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Red'/></Trigger></Style.Triggers></Style>",local:"Background='Blue'");var p=Preview(d,true);Check(Brush(p,Part(p))=="Blue");});
Test("style trigger outranks template self trigger",()=>{var d=Fixture("<Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Lime'/></Trigger>",style:"<Style TargetType='Button'><Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Red'/></Trigger></Style.Triggers></Style>");var p=Preview(d,true);Check(Brush(p,Part(p))=="Red");});
Test("template self trigger outranks style setter",()=>{var d=Fixture("<Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Red'/></Trigger>",style:"<Style TargetType='Button'><Setter Property='Background' Value='Blue'/></Style>");var p=Preview(d,true);Check(Brush(p,Part(p))=="Red");});
Test("named part trigger outranks its literal template value",()=>{var p=Preview(Fixture(visual:"<Border x:Name='Chrome' Background='Blue'/>"),true);Check(Brush(p,Part(p))=="Red");});
Test("SourceName resolves only within its template instance",()=>{var p=Preview(Fixture("<Trigger SourceName='Chrome' Property='Tag' Value='Active'><Setter TargetName='Chrome' Property='Background' Value='Red'/></Trigger>",visual:"<Border x:Name='Chrome' Tag='Active' Background='Blue'/>"));Check(Brush(p,Part(p))=="Red");});
Test("numeric condition uses numeric rather than text equality",()=>{var p=Preview(Fixture("<Trigger Property='Width' Value='120.0'><Setter TargetName='Chrome' Property='Background' Value='Red'/></Trigger>"));Check(Brush(p,Part(p))=="Red");});
Test("nullable checked condition is distinct from false",()=>{var p=Preview(Fixture("<Trigger Property='IsChecked' Value='{x:Null}'><Setter TargetName='Chrome' Property='Background' Value='Red'/></Trigger>",local:"IsChecked='{x:Null}'"));Check(Brush(p,Part(p))=="Red");});
Test("focus condition reacts without changing the saved node",()=>{var p=Preview(Fixture("<Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Chrome' Property='Background' Value='Red'/></Trigger>"),focus:true);Check(Brush(p,Part(p))=="Red");});
Test("inherited disabled value cannot be reenabled by a child local value",()=>{var d=Fixture("<Trigger Property='IsEnabled' Value='False'><Setter TargetName='Chrome' Property='Opacity' Value='0.4'/></Trigger>",local:"IsEnabled='True'");d=d with{Root=d.Root.Set("IsEnabled","False")};var p=Preview(d);Check(Target(p).Get("IsEnabled")=="False"&&Part(p).Get("Opacity")=="0.4");});
Test("inline brush TemplateBinding retains brush opacity",()=>{var d=Fixture("");var node=d.Root.Children[0] with{PropertyElements=[$"<Button.Background xmlns='{ns}'><SolidColorBrush Color='Blue' Opacity='0.5'/></Button.Background>"]};d=d with{Root=d.Root with{Children=[node]}};var p=Preview(d);Check(Brush(p,Part(p))!.Contains("Opacity=\"0.5\""));});
Test("object-valued trigger replaces attribute and element together",()=>{var p=Preview(Fixture("<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Chrome' Property='Background'><Setter.Value><SolidColorBrush Color='Red' Opacity='0.5'/></Setter.Value></Setter></Trigger>"),true);Check(Brush(p,Part(p))!.Contains("Opacity=\"0.5\""));});
Test("template-local resources resolve in trigger setters",()=>{var d=Fixture();var raw=d.Root.PropertyElements[0].Replace("<ControlTemplate.Triggers>","<ControlTemplate.Resources><SolidColorBrush x:Key='Hot' Color='Red'/></ControlTemplate.Resources><ControlTemplate.Triggers>").Replace("Value=\"Red\"","Value=\"{StaticResource Hot}\"");d=d with{Root=d.Root with{PropertyElements=[raw]}};var p=Preview(d,true);Check(Brush(p,Part(p))!.Contains("Red"));});
Test("multiple consumers keep independent names and input",()=>{var d=Fixture(local:"Background='Blue'");var second=d.Root.Children[0] with{Id=Guid.NewGuid()};second=second.Set(DesignNode.NameKey,"Other");d=d with{Root=d.Root with{Children=d.Root.Children.Add(second)}};var p=Preview(d,true);Check(Brush(p,Part(p))=="Red");var other=p.DescendantsAndSelf().Single(n=>n.Name=="Other_Chrome");Check(Brush(p,other)=="Blue");});
Test("BasedOn trigger order retains derived precedence",()=>{var d=Fixture("",resources:"<Style x:Key='Base' TargetType='Button'><Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Blue'/></Trigger></Style.Triggers></Style>",style:"<Style TargetType='Button' BasedOn='{StaticResource Base}'><Style.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='Red'/></Trigger></Style.Triggers></Style>");var p=Preview(d,true);Check(Brush(p,Part(p))=="Red");});
Test("explicit null Style suppresses implicit styles",()=>{var p=Preview(Fixture("",style:"<Style TargetType='Button'><Setter Property='Background' Value='Blue'/></Style>",local:"Style='{x:Null}'"));Check(Target(p).Get("Background")=="");});
Test("unsupported trigger collections are inert and diagnosed without losing visuals",()=>{var d=Fixture(hover+"<EventTrigger RoutedEvent='Loaded'/>",local:"Background='Blue'");var context=new DesignPreviewContext();context.Update(new(){Hovered=d.Root.Children[0].Id});var p=context.Resolve(d.Root);Check(context.Diagnostics.Any(s=>s.Contains("EventTrigger")));Check(Brush(p,Part(p))=="Blue");Check(XamlCodec.Write(d).Contains("EventTrigger"));});
Test("missing target rejects the complete collection",()=>{var d=Fixture(hover+"<Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Missing' Property='Opacity' Value='0.5'/></Trigger>",local:"Background='Blue'");var context=new DesignPreviewContext();context.Update(new(){Hovered=d.Root.Children[0].Id});var p=context.Resolve(d.Root);Check(context.Diagnostics.Any(s=>s.Contains("Missing")));Check(Brush(p,Part(p))=="Blue");});
foreach(var bad in new[]{"<Trigger Property='IsMouseOver' Value='True' Unknown='1'><Setter Property='Opacity' Value='0.5'/></Trigger>","<Trigger Property='IsMouseOver' Value='True'><Setter Property='Opacity' Value='NaN'/></Trigger>","<Trigger Property='IsEnabled' Value='False'><Setter Property='IsEnabled' Value='True'/></Trigger>","<DataTrigger Binding='{Binding X}' Value='True'><Setter Property='Opacity' Value='0.5'/></DataTrigger>","<Trigger Property='IsMouseOver' Value='True'><Setter Property='Template' Value='{StaticResource Other}'/></Trigger>"})
    Test("unsupported program rejects "+bad,()=>Reject(()=>PropertyTriggerProgram.Parse($"<ControlTemplate.Triggers xmlns='{ns}'>{bad}</ControlTemplate.Triggers>",true)));
Test("foreign namespace cannot impersonate a trigger",()=>Reject(()=>PropertyTriggerProgram.Parse($"<ControlTemplate.Triggers xmlns='{ns}'><Trigger xmlns='urn:other' Property='IsEnabled' Value='False'/></ControlTemplate.Triggers>",true)));
Test("XML external entities cannot execute",()=>{try{PropertyTriggerProgram.Parse("<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///etc/passwd'>]><x>&e;</x>",true);throw new Exception("Expected rejection");}catch(System.Xml.XmlException){}});
Test("compiled evaluation is allocation free with reused callbacks",()=>{var program=PropertyTriggerProgram.Parse($"<ControlTemplate.Triggers xmlns='{ns}'>{hover}</ControlTemplate.Triggers>",true);var count=0;Func<string?,string,string?> read=(_,_)=>"True";Action<PropertyTriggerProgram.Assignment> write=_=>count++;for(var i=0;i<20;i++)program.Evaluate(read,write);var before=GC.GetAllocatedBytesForCurrentThread();for(var i=0;i<10000;i++)program.Evaluate(read,write);warmBytes=GC.GetAllocatedBytesForCurrentThread()-before;Check(warmBytes==0&&count==10020);});
Test("unchanged context reuses the complete preview tree",()=>{var d=Fixture();var c=new DesignPreviewContext();var input=new PreviewInteractionState{Hovered=d.Root.Children[0].Id};c.Update(input);var first=c.Resolve(d.Root);Check(!c.Update(input with{})&&ReferenceEquals(first,c.Resolve(d.Root))&&c.Builds==1);c.Clear();Check(Part(c.Resolve(d.Root)).Id==Part(first).Id);});
Test("preview changes never create session revisions or undo entries",()=>{var d=Fixture();var session=new DesignSession(d);Preview(d,true,true);Check(session.Revision==0&&!session.CanUndo&&ReferenceEquals(session.Document,d));});
Test("trigger-generated border pixels reach the shared renderer",()=>{var d=Fixture(local:"Background='Blue'");using var renderer=new DesignRenderer();var p=Preview(d,true);using var image=SKBitmap.Decode(renderer.ExportPng(new LayoutEngine(renderer).Arrange(p),1));Check(image.GetPixel(45,65).Red>240&&image.GetPixel(45,65).Blue<10);});
Test("source and native roundtrip retain complete trigger declarations",()=>{var d=Fixture();var decoded=NativeDocumentCodec.Read(NativeDocumentCodec.Write(d));var again=XamlCodec.Parse(XamlCodec.Write(decoded)).Document;var p=Preview(again,true);Check(Brush(p,Part(p))=="Red");});
Directory.CreateDirectory("artifacts/verification");
File.WriteAllText("artifacts/verification/template-results.json",JsonSerializer.Serialize(new{passed,failed,warmEvaluations=10000,warmBytes,scope="Inert property/multi-trigger authoring preview; not complete WPF dependency properties or Blend UI pixels."}));
Console.WriteLine($"{passed} template tests passed; {failed} failed.");return failed==0?0:1;
