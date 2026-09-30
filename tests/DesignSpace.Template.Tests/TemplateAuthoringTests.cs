using System.Collections.Immutable;
using System.Xml.Linq;
using DesignSpace.Core;
using DesignSpace.Engine;
using DesignSpace.Xaml;

internal static class TemplateAuthoringTests
{
    public static (int Passed,int Failed) Run()
    {
        var passed=0;var failed=0;
        void Test(string name,Action run){try{run();passed++;Console.WriteLine("PASS template authoring: "+name);}catch(Exception e){failed++;Console.Error.WriteLine("FAIL template authoring: "+name+": "+e);}}
        void Check(bool value){if(!value)throw new Exception("Assertion failed");}
        void Reject(Action run){try{run();}catch(Exception e)when(e is InvalidDataException or InvalidOperationException or ArgumentException){return;}throw new Exception("Expected guarded rejection");}
        const string ns=DesignNode.PresentationNamespace,x=DesignNode.XamlNamespace;
        var button=DesignNode.Create("Button","Target",40,50,120,60).Set("Background","Blue");
        var blank=new DesignDocument{Root=DesignNode.Create("Canvas","Root",0,0,400,300) with{Children=[button]}};
        var saved=TemplateLibrary.Save(blank,TemplateLibrary.CreateInteractiveDefault());
        const string key="InteractiveButtonTemplate";
        Test("interactive example saves and applies as an editable resource",()=>{var applied=TemplateLibrary.Apply(saved,[button.Id],key);var c=new DesignPreviewContext();c.Update(new(){Hovered=button.Id});var p=c.Resolve(applied.Root);Check(c.Diagnostics.Count==0&&p.DescendantsAndSelf().Any(n=>n.Get("Background")=="#FF236CBD"));});
        Test("unchanged saved XML does not add an undo entry",()=>{var session=new DesignSession(saved);session.Execute("Save again",d=>TemplateLibrary.Save(d,TemplateLibrary.Read(d)[key]));Check(session.Revision==0&&!session.CanUndo&&ReferenceEquals(saved,session.Document));});
        Test("unchanged template application is a no-op",()=>{var d=TemplateLibrary.Apply(saved,[button.Id],key);Check(ReferenceEquals(d,TemplateLibrary.Apply(d,[button.Id],key)));});
        Test("local inline template is replaced rather than duplicated",()=>{var d=saved with{Root=saved.Root with{Children=[button with{PropertyElements=[$"<Button.Template xmlns='{ns}'><ControlTemplate TargetType='Button'><Border Background='Red'/></ControlTemplate></Button.Template>"]}]}};var result=TemplateLibrary.Apply(d,[button.Id],key);Check(result.Root.Children[0].PropertyElements.IsEmpty&&result.Root.Children[0].Get("Template")=="{StaticResource "+key+"}");});
        Test("mixed TargetType selection rejects before changing any node",()=>{var ellipse=DesignNode.Create("Ellipse","Other");var d=saved with{Root=saved.Root with{Children=[button,ellipse]}};var session=new DesignSession(d);Reject(()=>session.Execute("Apply",v=>TemplateLibrary.Apply(v,[button.Id,ellipse.Id],key)));Check(session.Revision==0&&ReferenceEquals(session.Document,d));});
        Test("inherited lock protects template assignment",()=>Reject(()=>TemplateLibrary.Apply(saved with{Root=saved.Root with{IsLocked=true}},[button.Id],key)));
        Test("root lock protects resource authoring",()=>Reject(()=>TemplateLibrary.Save(saved with{Root=saved.Root with{IsLocked=true}},TemplateLibrary.CreateDefault())));
        Test("unknown and empty targets reject",()=>{Reject(()=>TemplateLibrary.Apply(saved,[Guid.NewGuid()],key));Reject(()=>TemplateLibrary.Apply(saved,[],key));});
        Test("template assignment undo restores the exact source tree",()=>{var session=new DesignSession(saved);session.Execute("Apply",d=>TemplateLibrary.Apply(d,[button.Id],key));session.Undo();Check(ReferenceEquals(session.Document,saved));});
        Test("template-local resource keys do not leak into the root editor",()=>{var source=TemplateLibrary.CreateInteractiveDefault();var xml=XElement.Parse(source);xml.AddFirst(new XElement(XName.Get("ControlTemplate.Resources",ns),XElement.Parse(TemplateLibrary.CreateDefault("Nested"))));var d=TemplateLibrary.Save(blank,xml.ToString());Check(TemplateLibrary.Read(d).Count==1&&!TemplateLibrary.Read(d).ContainsKey("Nested"));});
        Test("explicit checked overrides remain outside the source document",()=>{var c=new DesignPreviewContext();c.Update(new(){Checked=ImmutableDictionary<Guid,bool?>.Empty.Add(button.Id,true)});Check(c.Resolve(blank.Root).Children[0].Get("IsChecked")=="True"&&blank.Root.Children[0].Get("IsChecked")=="");c.Clear();Check(c.Resolve(blank.Root).Children[0].Get("IsChecked")=="");});
        Test("invalid interaction identities reject atomically",()=>{var c=new DesignPreviewContext();Reject(()=>c.Update(new(){Hovered=Guid.Empty}));Check(c.State==PreviewInteractionState.Empty);});
        Test("style enablement cannot defeat an inherited disabled condition",()=>
        {
            var d=XamlCodec.Parse($"<Canvas xmlns='{ns}' xmlns:x='{x}' Width='400' Height='300' IsEnabled='False'><Canvas.Resources><Style TargetType='Button'><Setter Property='IsEnabled' Value='True'/><Style.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></Style.Triggers></Style></Canvas.Resources><Button x:Name='Target' Width='120' Height='60'/></Canvas>").Document;
            var preview=DesignPreview.Get(d.Root);Check(preview.Diagnostics.Count==0&&preview.Root.Children[0].Get("IsEnabled")=="False"&&preview.Root.Children[0].Get("Opacity")=="0.4");
        });
        Test("formatted template saves converge without stripping significant content",()=>
        {
            var xml=XElement.Parse(TemplateLibrary.CreateDefault("TextTemplate"));
            xml.Elements().Single().ReplaceWith(new XElement(XName.Get("TextBlock",ns),new XAttribute(XNamespace.Xml+"space","preserve"),"  content  "));
            var d=TemplateLibrary.Save(blank,xml.ToString());var text=TemplateLibrary.Read(d)["TextTemplate"];
            Check(XElement.Parse(text).Elements().Single().Value=="  content  ");
            Check(ReferenceEquals(d,TemplateLibrary.Save(d,text)));
            var edited=text.Replace("  content  ","  edited  ",StringComparison.Ordinal);
            var next=TemplateLibrary.Save(d,edited);Check(!ReferenceEquals(d,next));
            Check(XElement.Parse(TemplateLibrary.Read(next)["TextTemplate"]).Elements().Single().Value=="  edited  ");
        });
        return(passed,failed);
    }
}
