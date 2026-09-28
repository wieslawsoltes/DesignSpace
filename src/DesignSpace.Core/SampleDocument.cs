using System.Collections.Immutable;
namespace DesignSpace.Core;

public static class SampleDocument
{
    public static DesignDocument Create()
    {
        DesignNode Shape(string type,string name,double x,double y,double w,double h,string color) => DesignNode.Create(type,name,x,y,w,h).Set(type is "Rectangle" or "Ellipse" ? "Fill" : "Background",color);
        DesignNode Text(string name,string text,double x,double y,double size,string color = "#FF202838") => DesignNode.Create("TextBlock",name,x,y,600,size*1.5).Set("Text",text).Set("FontSize",size).Set("Foreground",color);
        var button = Shape("Button","ExploreButton",64,390,168,44,"#FF0078D4").Set("Content","Explore collection").Set("Foreground","#FFFFFFFF").Set("CornerRadius","3");
        var card = Shape("Border","FeatureCard",570,90,292,344,"#FFF0F4FA").Set("CornerRadius","12") with { Children = [
            Shape("Canvas","CardLayout",0,0,292,344,"Transparent") with { Children = [
                Shape("Ellipse","OrbitOuter",48,40,196,196,"#FFDDE8F8"),
                Shape("Ellipse","OrbitMiddle",78,70,136,136,"#FFB8D5F4"),
                Shape("Ellipse","OrbitCore",109,101,74,74,"#FF0078D4"),
                Text("CardTitle","Make every interaction count.",24,260,16),
                Text("CardCaption","Designed with DesignSpace",24,292,12,"#FF63738A")
            ] }
        ] };
        var root = Shape("Canvas","LayoutRoot",0,0,960,560,"#FFFFFFFF") with { Children = [
            Text("Brand","NORTHSTAR",32,22,16),
            Text("Navigation","Discover          Collections          About",591,26,12,"#FF63738A"),
            Shape("Rectangle","HeaderRule",32,64,896,1,"#FFE6EAF0"),
            Text("Eyebrow","A LITTLE MOTION. A LOT OF POSSIBILITY.",64,136,11,"#FF0078D4"),
            Text("Headline","Ideas, brought to life.",62,185,36),
            Text("DescriptionLine1","Create thoughtful experiences with beautiful",64,258,17,"#FF63738A"),
            Text("DescriptionLine2","details. Start with an idea. Make it move.",64,286,17,"#FF63738A"),
            button, card,
            Shape("Rectangle","FooterRule",32,492,896,1,"#FFE6EAF0"),
            Text("Footer","01  /  INTERACTION STUDIES",32,516,11,"#FF63738A"),
            Text("FooterRight","CRAFTED WITH INTENTION",718,516,11,"#FF63738A")
        ] };
        var orbit = root.DescendantsAndSelf().Single(n => n.Name == "OrbitCore");
        return new DesignDocument { Root = root,
            Storyboards = [new(Guid.NewGuid(),"Intro",2,[new(button.Id,"Opacity",[new(0,0),new(.6,1,"EaseOut")]),new(orbit.Id,"Rotation",[new(0,0),new(2,360,"EaseInOut")])])],
            States = [new("Normal",[]),new("PointerOver",[new(button.Id,"Background","#FF106EBE")]),new("Pressed",[new(button.Id,"Background","#FF005A9E")]),new("Disabled",[new(button.Id,"Opacity","0.4")])]
        };
    }
}
