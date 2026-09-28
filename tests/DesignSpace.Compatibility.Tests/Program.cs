using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;

var passed=0;var failed=0;
void Test(string name,Action action) { try { action();passed++;Console.WriteLine("PASS "+name); } catch(Exception ex) { failed++;Console.Error.WriteLine("FAIL "+name+": "+ex); } }
void Check(bool condition) { if(!condition) throw new Exception("Assertion failed"); }
void Equal<T>(T expected,T actual) { if(!EqualityComparer<T>.Default.Equals(expected,actual)) throw new Exception($"Expected {expected}, got {actual}"); }
void Reject(Action action) { try { action(); } catch(Exception) { return; } throw new Exception("Expected rejection"); }
var ns=DesignNode.PresentationNamespace;var x=DesignNode.XamlNamespace;
string Wrap(string content,string attributes="")=>$"<Canvas xmlns='{ns}' xmlns:x='{x}' x:Name='Root' Width='960' Height='640' {attributes}>{content}</Canvas>";
DesignDocument Sample()=>SampleDocument.Create();
DesignDocument RoundTrip(DesignDocument d)=>XamlCodec.Parse(XamlCodec.Write(d)).Document;
Test("native immutable JSON without reflection",()=>{var d=Sample();var text=NativeDocumentCodec.Write(d);var p=NativeDocumentCodec.Read(text);Equal(d.Root.Id,p.Root.Id);Equal(d.Root.Children.Length,p.Root.Children.Length);Equal(d.Storyboards[0].Tracks[0].TargetId,p.Storyboards[0].Tracks[0].TargetId);Equal(text,NativeDocumentCodec.Write(p));});
Test("native preserves locks",()=>{var d=Sample();d=d with { Root=d.Root with { IsLocked=true } };Check(NativeDocumentCodec.Read(NativeDocumentCodec.Write(d)).Root.IsLocked);});
Test("XAML node count round-trip",()=>Equal(Sample().Root.DescendantsAndSelf().Count(),RoundTrip(Sample()).Root.DescendantsAndSelf().Count()));
Test("rotation animation round-trip",()=>{var p=RoundTrip(Sample());Equal(2,p.Storyboards[0].Tracks.Length);Equal("Rotation",p.Storyboards[0].Tracks[1].Property);Equal(360d,p.Storyboards[0].Tracks[1].Keys[1].Value);});
Test("animated zero rotation emits a transform",()=>{var xml=XElement.Parse(XamlCodec.Write(Sample()));var node=xml.Descendants().Single(e=>(string?)e.Attribute(XName.Get("Name",x))=="OrbitCore");Check(node.Elements().Any(e=>e.Name.LocalName=="Ellipse.RenderTransform"));});
Test("easing round-trip",()=>Equal("EaseInOut",RoundTrip(Sample()).Storyboards[0].Tracks[1].Keys[1].Easing));
Test("visual-state setter round-trip",()=>{var d=RoundTrip(Sample());Equal(4,d.States.Length);Equal("#FF005A9E",d.States.Single(s=>s.Name=="Pressed").Setters[0].Value);});
Test("empty storyboard survives",()=>{var d=DesignDocument.Empty() with { Storyboards=[new(Guid.NewGuid(),"Empty",2,[])] };Equal(1,RoundTrip(d).Storyboards.Length);});
Test("native rejects unsupported version",()=>Reject(()=>NativeDocumentCodec.Read(NativeDocumentCodec.Write(Sample()).Replace("\"formatVersion\": 1","\"formatVersion\": 99"))));
Test("DTD and external entities prohibited",()=>Reject(()=>XamlCodec.Parse("<!DOCTYPE Canvas [<!ENTITY bad SYSTEM 'file:///etc/passwd'>]>"+Wrap("&bad;"))));
Test("malformed XAML rejected",()=>Reject(()=>XamlCodec.Parse("<Canvas>")));
Test("excessive nesting rejected",()=>Reject(()=>XamlCodec.Parse(Wrap(string.Concat(Enumerable.Repeat("<Canvas>",70))+string.Concat(Enumerable.Repeat("</Canvas>",70))))));
Test("custom namespaced element preserved",()=>{var p=XamlCodec.Parse(Wrap("<custom:Widget custom:Mode='Safe' />","xmlns:custom='using:Example'"));Check(p.Diagnostics.Any());Check(XamlCodec.Write(p.Document).Contains("Widget"));Equal("using:Example",p.Document.Root.Children[0].Namespace);});
Test("gradient property element preserved",()=>{var p=XamlCodec.Parse(Wrap("<Rectangle Width='20' Height='20'><Rectangle.Fill><LinearGradientBrush><GradientStop Color='Red' Offset='0'/><GradientStop Color='Blue' Offset='1'/></LinearGradientBrush></Rectangle.Fill></Rectangle>"));Check(XamlCodec.Write(p.Document).Contains("GradientStop"));});
Test("unsupported storyboard semantics preserved",()=>{var text=Wrap("<Canvas.Resources><Storyboard x:Key='Reverse' AutoReverse='True'><DoubleAnimation Storyboard.TargetName='R' Storyboard.TargetProperty='Opacity' To='1'/></Storyboard></Canvas.Resources><Rectangle x:Name='R' Width='10' Height='10'/>");var d=XamlCodec.Parse(text).Document;Equal(0,d.Storyboards.Length);Check(XamlCodec.Write(d).Contains("AutoReverse=\"True\""));});
Test("resource dictionary wrapper preserved",()=>{var d=XamlCodec.Parse(Wrap("<Canvas.Resources><ResourceDictionary><SolidColorBrush x:Key='Accent' Color='Red'/><Storyboard x:Key='Empty' Duration='0:0:2'/></ResourceDictionary></Canvas.Resources>")).Document;Equal(1,d.Storyboards.Length);var xml=XElement.Parse(XamlCodec.Write(d));Equal(2,xml.Descendants().Single(e=>e.Name.LocalName=="ResourceDictionary").Elements().Count());});
Test("source reconciliation retains named identities",()=>{var original=Sample();var parsed=RoundTrip(original);var d=XamlCodec.Reconcile(original,parsed);Equal(original.Root.Id,d.Root.Id);Equal(original.Storyboards[0].Id,d.Storyboards[0].Id);Equal(original.Storyboards[0].Tracks[0].TargetId,d.Storyboards[0].Tracks[0].TargetId);});
Test("source reconciliation retains lock state",()=>{var original=Sample();original=original with { Root=original.Root with { IsLocked=true } };Check(XamlCodec.Reconcile(original,RoundTrip(original)).Root.IsLocked);});
Test("sample data binding resolution is non-mutating",()=>{var n=DesignNode.Create("TextBlock","Label").Set("Text","{Binding Customer.Name}");var root=DesignData.Set(DesignNode.Create("Canvas","Root") with { Children=[n] },"{\"Customer\":{\"Name\":\"Ada\"}}");Equal("Ada",DesignData.Resolve(root).Children[0].Get("Text"));Equal("{Binding Customer.Name}",root.Children[0].Get("Text"));});
Test("sample data survives XAML",()=>{var d=DesignDocument.Empty();d=d with { Root=DesignData.Set(d.Root,"{\"Title\":\"Hello\"}") };Check(RoundTrip(d).Root.Get(DesignData.DataKey).Contains("Hello"));});
Test("invalid sample data rejected",()=>Reject(()=>DesignData.Set(DesignDocument.Empty().Root,"[1,2]")));
Test("unsupported markup extensions remain inert",()=>{var text="{danger:Run Command=Launch}";var d=XamlCodec.Parse(Wrap("<TextBlock Text='"+text+"'/>"));Equal(text,d.Document.Root.Children[0].Get("Text"));});
Console.WriteLine($"{passed} passed; {failed} failed.");return failed==0 ? 0 : 1;
