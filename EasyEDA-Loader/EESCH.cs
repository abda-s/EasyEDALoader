using EDP;
using SCH;
using System.Windows.Forms;

namespace EasyEDA_Loader
{
    public class EESCH
    {
        public static ISch_Lib GetCurrentSchLibrary()
        {
            var schDoc = AltiumApi.GlobalVars.SCHServer.GetCurrentSchDocument();
            if (schDoc == null)
            {
                MessageBox.Show("This is not a SCH library document", "EasyEDA Loader Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }
            if (schDoc != null && schDoc.GetState_ObjectId() != SCH.TObjectId.eSchLib)
            {
                MessageBox.Show("Open schematic library", "EasyEDA Loader Error", MessageBoxButtons.OK, MessageBoxIcon.Hand);
                return null;
            }

            return schDoc as ISch_Lib;
        }
        public static ISch_Component CreateComponent(string name, string desc, string designator)
        {
            var schComponent = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.eSchComponent, SCH.TObjectCreationMode.eCreate_Default) as ISch_Component;
            if (schComponent == null)
                return null;

            schComponent.SetState_CurrentPartID(1);
            schComponent.SetState_DisplayMode(0);
            schComponent.SetState_LibReference(name);
            schComponent.GetState_SchDesignator().SetState_Text(designator);
            schComponent.SetState_ComponentDescription(desc);
            return schComponent;
        }


        public static void AddParameter(ISch_Component c, string name, string value)
        {
            ISch_Parameter param = c.AddSchParameter();
            param.SetState_Text(value);
            param.SetState_Name(name);
            param.SetState_ShowName(false);
            param.SetState_IsHidden(true);
        }

        public static int RGB(int r, int g, int b)
        {
            return (b << 16) | (g << 8) | r;
        }

        public class FontInfo
        {
            public int Size { get; set; }
            public int Rotation { get; set; } = 0;
            public bool Underline { get; set; } = false;
            public bool Italic { get; set; } = false;
            public bool Bold { get; set; } = false;
            public bool Strikout { get; set; } = false;
            public string Name { get; set; }
            public int Color { get; set; } = 0;
        }

        public static ISch_Pin CreatePin(ISch_Lib schLib, ISch_Component c, double x, double y, string designator, string name, TRotationBy90 orientation, double length, TPinElectrical pinType, bool showName, FontInfo fontInfo)
        {
            var schPin = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.ePin, SCH.TObjectCreationMode.eCreate_Default) as ISch_Pin;
            if (schPin == null)
                return null;

            schPin.SetState_Location(new DXP.Point
            {
                X = AltiumApi.MilsToCoord(x),
                Y = AltiumApi.MilsToCoord(y)
            });
            schPin.SetState_PinLength(AltiumApi.MilsToCoord(length));
            schPin.SetState_Color(0);
            schPin.SetState_Orientation(orientation);
            schPin.SetState_Designator(designator);
            schPin.SetState_Name(name);
            schPin.SetState_Electrical(pinType);
            schPin.SetState_ShowName(showName);
            schPin.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
            schPin.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());

            if (fontInfo != null)
            {
                int fontId = AltiumApi.GlobalVars.SCHServer.GetState_FontManager().GetFontID(fontInfo.Size, fontInfo.Rotation, fontInfo.Underline, fontInfo.Italic, fontInfo.Bold, fontInfo.Strikout, fontInfo.Name);
                schPin.SetState_Designator_FontMode(TPinItemMode.ePinItemMode_Custom);
                schPin.SetState_Designator_CustomFontID(fontId);
                schPin.SetState_Designator_CustomColor(fontInfo.Color);
            }

            c.AddSchObject(schPin);
            return schPin;
        }

        public static ISch_Pin CreateLeftPin(ISch_Lib schLib, ISch_Component c, double x, double y, string designator, string name, double length, TPinElectrical pinType, bool showName, FontInfo fontInfo)
        {
            return CreatePin(schLib, c, x, y, designator, name, TRotationBy90.eRotate180, length, pinType, showName, fontInfo);
        }
        public static ISch_Pin CreateRightPin(ISch_Lib schLib, ISch_Component c, double x, double y, string designator, string name, double length, TPinElectrical pinType, bool showName, FontInfo fontInfo)
        {
            return CreatePin(schLib, c, x, y, designator, name, TRotationBy90.eRotate0, length, pinType, showName, fontInfo);
        }
        public static ISch_Pin CreateTopPin(ISch_Lib schLib, ISch_Component c, double x, double y, string designator, string name, double length, TPinElectrical pinType, bool showName, FontInfo fontInfo)
        {
            return CreatePin(schLib, c, x, y, designator, name, TRotationBy90.eRotate90, length, pinType, showName, fontInfo);
        }
        public static ISch_Pin CreateBottomPin(ISch_Lib schLib, ISch_Component c, double x, double y, string designator, string name, double length, TPinElectrical pinType, bool showName, FontInfo fontInfo)
        {
            return CreatePin(schLib, c, x, y, designator, name, TRotationBy90.eRotate270, length, pinType, showName, fontInfo);
        }

        public static void CreateRectangle(ISch_Lib schLib, ISch_Component c, double x1, double y1, double x2, double y2)
        {
            var rect = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.eRectangle, SCH.TObjectCreationMode.eCreate_Default) as ISch_Rectangle;
            if (rect == null)
                return;
            rect.SetState_LineWidth(TSize.eSmall);
            rect.SetState_Location(new DXP.Point
            {
                X = AltiumApi.MilsToCoord(x1),
                Y = AltiumApi.MilsToCoord(y1)
            });
            rect.SetState_Corner(new DXP.Point
            {
                X = AltiumApi.MilsToCoord(x2),
                Y = AltiumApi.MilsToCoord(y2)
            });

            rect.SetState_Color(RGB(128, 0, 0));
            rect.SetState_AreaColor(RGB(255, 255, 176));
            rect.SetState_IsSolid(true);
            rect.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
            rect.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
            c.AddSchObject(rect);
        }

        public static void CreateLine(ISch_Lib schLib, ISch_Component c, double x1, double y1, double x2, double y2)
        {
            var line = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.eLine, SCH.TObjectCreationMode.eCreate_Default) as ISch_Line;
            if (line == null)
                return;

            line.SetState_LineWidth(TSize.eSmall);
            line.SetState_Location(new DXP.Point
            {
                X = AltiumApi.MilsToCoord(x1),
                Y = AltiumApi.MilsToCoord(y1)
            });
            line.SetState_Corner(new DXP.Point
            {
                X = AltiumApi.MilsToCoord(x2),
                Y = AltiumApi.MilsToCoord(y2)
            });
            line.SetState_Color(0);
            line.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
            line.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
            c.AddSchObject(line);
        }

        // points are (x, y) pairs in mils
        public static void CreatePolyline(ISch_Lib schLib, ISch_Component c, int color, params double[] points)
        {
            var poly = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.ePolyline, SCH.TObjectCreationMode.eCreate_Default) as ISch_Polyline;
            if (poly == null)
                return;

            int count = points.Length / 2;
            poly.SetState_LineWidth(TSize.eSmall);
            poly.SetState_VerticesCount(count);
            for (int i = 0; i < count; i++)
            {
                poly.SetState_Vertex(i + 1, new DXP.Point
                {
                    X = AltiumApi.MilsToCoord(points[i * 2]),
                    Y = AltiumApi.MilsToCoord(points[i * 2 + 1])
                });
            }
            poly.SetState_Color(color);
            poly.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
            poly.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
            c.AddSchObject(poly);
        }

        // Rectangle with explicit colours; solid=false leaves it hollow.
        public static void CreateRectangle(ISch_Lib schLib, ISch_Component c, double x1, double y1, double x2, double y2, int color, int areaColor, bool solid)
        {
            var rect = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.eRectangle, SCH.TObjectCreationMode.eCreate_Default) as ISch_Rectangle;
            if (rect == null)
                return;
            rect.SetState_LineWidth(TSize.eSmall);
            rect.SetState_Location(new DXP.Point { X = AltiumApi.MilsToCoord(x1), Y = AltiumApi.MilsToCoord(y1) });
            rect.SetState_Corner(new DXP.Point { X = AltiumApi.MilsToCoord(x2), Y = AltiumApi.MilsToCoord(y2) });
            rect.SetState_Color(color);
            rect.SetState_AreaColor(areaColor);
            rect.SetState_IsSolid(solid);
            rect.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
            rect.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
            c.AddSchObject(rect);
        }

        // points are (x, y) pairs in mils
        public static void CreatePolygon(ISch_Lib schLib, ISch_Component c, int color, int areaColor, bool solid, params double[] points)
        {
            var poly = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.ePolygon, SCH.TObjectCreationMode.eCreate_Default) as ISch_Polygon;
            if (poly == null)
                return;

            int count = points.Length / 2;
            poly.SetState_LineWidth(TSize.eSmall);
            poly.SetState_VerticesCount(count);
            for (int i = 0; i < count; i++)
            {
                poly.SetState_Vertex(i + 1, new DXP.Point
                {
                    X = AltiumApi.MilsToCoord(points[i * 2]),
                    Y = AltiumApi.MilsToCoord(points[i * 2 + 1])
                });
            }
            poly.SetState_Color(color);
            poly.SetState_AreaColor(areaColor);
            poly.SetState_IsSolid(solid);
            poly.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
            poly.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
            c.AddSchObject(poly);
        }

        // Circle when rx == ry. Location is the centre.
        public static void CreateEllipse(ISch_Lib schLib, ISch_Component c, double cx, double cy, double rx, double ry, int color, int areaColor, bool solid)
        {
            var ellipse = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.eEllipse, SCH.TObjectCreationMode.eCreate_Default) as ISch_Ellipse;
            if (ellipse == null)
                return;
            ellipse.SetState_LineWidth(TSize.eSmall);
            ellipse.SetState_Location(new DXP.Point { X = AltiumApi.MilsToCoord(cx), Y = AltiumApi.MilsToCoord(cy) });
            ellipse.SetState_Radius(AltiumApi.MilsToCoord(rx));
            ellipse.SetState_SecondaryRadius(AltiumApi.MilsToCoord(ry));
            ellipse.SetState_Color(color);
            ellipse.SetState_AreaColor(areaColor);
            ellipse.SetState_IsSolid(solid);
            ellipse.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
            ellipse.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
            c.AddSchObject(ellipse);
        }

        // Drawn counter-clockwise from startAngle to endAngle (degrees, 0 = +X).
        public static void CreateArc(ISch_Lib schLib, ISch_Component c, double cx, double cy, double rx, double ry, double startAngle, double endAngle, int color)
        {
            var location = new DXP.Point { X = AltiumApi.MilsToCoord(cx), Y = AltiumApi.MilsToCoord(cy) };
            if (System.Math.Abs(rx - ry) < 0.5)
            {
                var arc = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.eArc, SCH.TObjectCreationMode.eCreate_Default) as ISch_Arc;
                if (arc == null)
                    return;
                arc.SetState_LineWidth(TSize.eSmall);
                arc.SetState_Location(location);
                arc.SetState_Radius(AltiumApi.MilsToCoord(rx));
                arc.SetState_StartAngle(startAngle);
                arc.SetState_EndAngle(endAngle);
                arc.SetState_Color(color);
                arc.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
                arc.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
                c.AddSchObject(arc);
            }
            else
            {
                var arc = AltiumApi.GlobalVars.SCHServer.SchObjectFactory(SCH.TObjectId.eEllipticalArc, SCH.TObjectCreationMode.eCreate_Default) as ISch_EllipticalArc;
                if (arc == null)
                    return;
                arc.SetState_LineWidth(TSize.eSmall);
                arc.SetState_Location(location);
                arc.SetState_Radius(AltiumApi.MilsToCoord(rx));
                arc.SetState_SecondaryRadius(AltiumApi.MilsToCoord(ry));
                arc.SetState_StartAngle(startAngle);
                arc.SetState_EndAngle(endAngle);
                arc.SetState_Color(color);
                arc.SetState_OwnerPartId(schLib.GetState_CurrentSchComponentPartId());
                arc.SetState_OwnerPartDisplayMode(schLib.GetState_CurrentSchComponentDisplayMode());
                c.AddSchObject(arc);
            }
        }

        public static void AssignFootprint(ISch_Component c, string libraryPath, string modelName, string modelMapping)
        {
            var modelType = "PCBLIB";
            var model = c.AddSchImplementation();
            model.ClearAllDatafileLinks();
            model.SetState_MapAsString(modelMapping);
            model.SetState_ModelName(modelName);
            model.SetState_ModelType(modelType);
            model.AddDataFileLink(modelName, libraryPath, modelType);
            model.SetState_IsCurrent(true);
        }

    }
}
