using EDP;
using SCH;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using static EasyEDA_Loader.EeSymbolShape;

namespace EasyEDA_Loader
{
    public class AltiumSymbolRectangle
    {
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        public double Width
        {
            get { return X2 - X1; }
        }
        public double Height
        {
            get { return Y2 - Y1; }
        }
    }

    public enum PinOrientation
    {
        Top = 0,
        Left,
        Right,
        Bottom
    }

    public class AltiumSymbolPin
    {
        static public TPinElectrical FromEEPinType(EasyedaPinType pinType)
        {
            switch (pinType)
            {
                case EasyedaPinType.Bidirectional:
                    return TPinElectrical.eElectricIO;
                case EasyedaPinType.Input:
                    return TPinElectrical.eElectricInput;
                case EasyedaPinType.Output:
                    return TPinElectrical.eElectricOutput;
                case EasyedaPinType.Power:
                    return TPinElectrical.eElectricPower;
                default:
                    return TPinElectrical.eElectricPassive;
            }
        }
        static public TRotationBy90 FromOrientation(PinOrientation orientation)
        {
            switch (orientation)
            {
                case PinOrientation.Top:
                    return TRotationBy90.eRotate90;
                case PinOrientation.Right:
                    return TRotationBy90.eRotate0;
                case PinOrientation.Left:
                    return TRotationBy90.eRotate180;
                case PinOrientation.Bottom:
                    return TRotationBy90.eRotate270;
                default:
                    return TRotationBy90.eRotate180;
            }
        }

        public double X { get; set; }
        public double Y { get; set; }
        public string Designator { get; set; }
        public string Name { get; set; }
        public TRotationBy90 Orientation { get; set; }
        public double Length { get; set; }
        public TPinElectrical PinType { get; set; }
        public bool ShowName { get; set; }
    }

    public class SymbolDrawing
    {
        static void DistributeEvenly<T>(List<T> source, List<List<T>> targets)
        {
            // Keep track of how many items are in each target list
            var counts = targets.Select(l => l.Count).ToList();

            foreach (var item in source)
            {
                // Find the index of the smallest list
                int minIndex = 0;
                int minCount = counts[0];

                for (int i = 1; i < counts.Count; i++)
                {
                    if (counts[i] < minCount)
                    {
                        minIndex = i;
                        minCount = counts[i];
                    }
                }

                // Add the item to the smallest list
                targets[minIndex].Add(item);
                counts[minIndex]++;
            }
        }


        static public void DrawAltiumRectangle(Canvas c, AltiumSymbolRectangle arect)
        {
            var rect = new Rectangle
            {
                Width = arect.X2 - arect.X1,
                Height = arect.Y2 - arect.Y1,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
            };
            Canvas.SetLeft(rect, arect.X1);
            Canvas.SetTop(rect, arect.Y1);

            c.Children.Add(rect);
        }

        static public void DrawAltiumPin(Canvas c, AltiumSymbolPin pin, double lineLength)
        {
            double x2 = pin.X, y2 = pin.Y;

            double angle = 0;
            Point anchorPoint = new Point(0, 0.5);
            switch (pin.Orientation)
            {
                case TRotationBy90.eRotate90:
                    x2 = pin.X;
                    y2 = pin.Y - lineLength;
                    anchorPoint = new Point(1.0, 0.5);
                    angle = -90;
                    break;
                case TRotationBy90.eRotate180:
                    x2 = pin.X - lineLength;
                    y2 = pin.Y;
                    anchorPoint = new Point(0.0, 0.5);
                    angle = 0;
                    break;
                case TRotationBy90.eRotate0:
                    x2 = pin.X + lineLength;
                    y2 = pin.Y;
                    anchorPoint = new Point(0.0, 0.5);
                    angle = 0;
                    break;
                case TRotationBy90.eRotate270:
                    x2 = pin.X;
                    y2 = pin.Y + lineLength;
                    anchorPoint = new Point(1.0, 0.5);
                    angle = 90;
                    break;
                default:
                    break;
            }

            var line = new Line
            {
                X1 = pin.X,
                Y1 = pin.Y,
                X2 = x2,
                Y2 = y2,
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            };

            TextBlock label = new TextBlock
            {
                Text = pin.Designator,
                FontSize = 50,
                Foreground = Brushes.Red,
                RenderTransformOrigin = anchorPoint,
            };

            label.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = label.DesiredSize.Width;
            double textHeight = label.DesiredSize.Height;

            double margin = 50; // distance in pixels
            if (pin.Orientation != TRotationBy90.eRotate180)
            {

                double angleRadians = angle * Math.PI / 180;

                // Offset text away from line start, along line direction
                double offsetX = -Math.Cos(angleRadians) * margin;
                double offsetY = -Math.Sin(angleRadians) * margin;

                Canvas.SetLeft(label, line.X1 - textWidth + offsetX);
                Canvas.SetTop(label, line.Y1 - textHeight / 2 + offsetY);
            }
            else
            {
                Canvas.SetLeft(label, line.X1 + margin);
                Canvas.SetTop(label, line.Y1 - textHeight / 2);
            }


            label.RenderTransform = new RotateTransform(angle);
            c.Children.Add(label);

            c.Children.Add(line);
        }

        static public (AltiumSymbolRectangle, List<AltiumSymbolPin>) LayoutPins(List<EeSymbolShape> Shapes, int widthMargin = 8, int heightMargin = 8, int gridSize = 100)
        {
            List<List<EeSymbolPin>> items = new()
            {
                // Top
                Shapes.OfType<EeSymbolPin>().Where(shape => shape.Name.Rotation == 270 && shape.Name.TextAnchor == "end").OrderBy(s => s.Settings.PosX).ToList(),
                // Left
                Shapes.OfType<EeSymbolPin>().Where(shape => shape.Name.Rotation == 0 && shape.Name.TextAnchor == "start").OrderBy(s => s.Settings.PosY).ToList(),
                // Right
                Shapes.OfType<EeSymbolPin>().Where(shape => shape.Name.Rotation == 0 && shape.Name.TextAnchor == "end").OrderBy(s => s.Settings.PosY).ToList(),
                // Bottom
                Shapes.OfType<EeSymbolPin>().Where(shape => shape.Name.Rotation == 270 && shape.Name.TextAnchor == "start").OrderBy(s => s.Settings.PosX).ToList()
            };

            // If there were uncategorized pins, put them somewhere
            var uncategorized = Shapes.OfType<EeSymbolPin>().Except(items[0].Union(items[1]).Union(items[2]).Union(items[3])).ToList();

            var populated = items.Where(item => item.Count != 0).OrderBy(item => item.Count).ToList();
            if (populated.Count == 0) // Everything was uncategorized? Weird, add everything to the left
            {
                items[1].AddRange(uncategorized);
            }
            else if (populated.Count == 1) // If there's only one direction, just add everything to it
            {
                populated.FirstOrDefault().AddRange(uncategorized);
            }
            else // There are multiple available directions, distribute them starting with the least populated
            {
                DistributeEvenly(uncategorized, items);
            }

            // Select the largest of the two sides, these will determine the dimensions of the encompassing rect
            var widthPins = items[0].Count > items[3].Count ? items[0] : items[3];
            var heightPins = items[1].Count > items[2].Count ? items[1] : items[2];

            var halfWidthMargin = widthMargin / 2;
            var halfHeightMargin = heightMargin / 2;

            if (items[0].Count == 0 && items[3].Count == 0) // Only Left/Right
            {
                heightMargin = 0;
                halfHeightMargin = heightMargin / 2;
            }
            else if (items[1].Count == 0 && items[2].Count == 0) // Only Top/Bottom
            {
                widthMargin = 0;
                halfWidthMargin = widthMargin / 2;
            }

            var altiumRect = new AltiumSymbolRectangle
            {
                X1 = 0,
                Y1 = 0,
                X2 = (widthPins.Count + widthMargin) * gridSize + gridSize,
                Y2 = (heightPins.Count + heightMargin) * gridSize + gridSize,
            };

            List<(double x, double y)> offsets = new()
            {
                (halfWidthMargin * gridSize, 0),
                (0, halfHeightMargin * gridSize + gridSize),
                (altiumRect.Width, halfHeightMargin * gridSize + gridSize),
                (halfWidthMargin * gridSize, altiumRect.Height)
            };

            List<AltiumSymbolPin> pins = new();
            for (var i = 0; i < items.Count; ++i)
            {
                double offset_x = offsets[i].x, offset_y = offsets[i].y;
                for (var p = 0; p < items[i].Count; ++p)
                {
                    var x = offset_x;
                    var y = offset_y;
                    switch ((PinOrientation)i)
                    {
                        case PinOrientation.Top:
                            x += p * gridSize;
                            break;
                        case PinOrientation.Left:
                            y += p * gridSize;
                            break;
                        case PinOrientation.Right:
                            y += p * gridSize;
                            break;
                        case PinOrientation.Bottom:
                            x += p * gridSize;
                            break;
                        default:
                            break;
                    }

                    pins.Add(new AltiumSymbolPin
                    {
                        X = x,
                        Y = y,
                        Orientation = AltiumSymbolPin.FromOrientation((PinOrientation)i),
                        Designator = items[i][p].Settings.SpicePinNumber,
                        Name = items[i][p].Name.Text,
                        Length = 200,
                        ShowName = items[i][p].Name.IsDisplayed,
                        PinType = AltiumSymbolPin.FromEEPinType(items[i][p].Settings.Type)
                    });
                }
            }
            return (altiumRect, pins);
        }

        // Preview colours, matched to the dialog's dark wells.
        private static readonly Brush PreviewBody = Frozen(Color.FromRgb(0xD9, 0x6B, 0x6B));
        private static readonly Brush PreviewBodyFill = Frozen(Color.FromRgb(0x4A, 0x45, 0x2E));
        private static readonly Brush PreviewLine = Frozen(Color.FromRgb(0x7A, 0xA7, 0xFF));
        private static readonly Brush PreviewPin = Frozen(Color.FromRgb(0xC8, 0xC8, 0xC8));
        private static readonly Brush PreviewName = Frozen(Color.FromRgb(0xE6, 0xE6, 0xE6));
        private static readonly Brush PreviewNumber = Frozen(Color.FromRgb(0xA8, 0xA8, 0xA8));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        // Draws the converted symbol exactly as it will be created in Altium (1 canvas unit = 1 mil).
        static public void DrawComponent(Canvas c, SymbolData symbol)
        {
            if (symbol?.Shapes == null)
                return;
            c.Children.Clear();

            var g = SymbolConverter.Convert(symbol);
            if (g.Pins.Count == 0 && g.Rects.Count == 0 && g.Polys.Count == 0 && g.Ellipses.Count == 0 && g.Arcs.Count == 0)
                return;

            var (minX, minY, maxX, maxY) = g.Bounds();
            const double margin = 150; // room for pin names/numbers sticking out
            double offX = -minX + margin, offY = maxY + margin;
            Point P(double x, double y) => new Point(x + offX, offY - y); // Altium Y up -> canvas Y down

            // Invisible frame so the zoom helper fits the whole symbol (it measures Left/Top/size).
            var frame = new Rectangle { Width = maxX - minX + 2 * margin, Height = maxY - minY + 2 * margin, Fill = Brushes.Transparent };
            Canvas.SetLeft(frame, 0);
            Canvas.SetTop(frame, 0);
            c.Children.Add(frame);

            const double stroke = 8;

            foreach (var r in g.Rects)
            {
                var tl = P(r.X1, r.Y2);
                var rect = new Rectangle
                {
                    Width = Math.Max(1, r.X2 - r.X1),
                    Height = Math.Max(1, r.Y2 - r.Y1),
                    Stroke = PreviewBody,
                    StrokeThickness = stroke,
                    Fill = r.Fill == SymbolConverter.FillKind.None ? null : r.Fill == SymbolConverter.FillKind.Solid ? PreviewBody : PreviewBodyFill,
                };
                Canvas.SetLeft(rect, tl.X);
                Canvas.SetTop(rect, tl.Y);
                c.Children.Add(rect);
            }

            foreach (var e in g.Ellipses)
            {
                var tl = P(e.Cx - e.Rx, e.Cy + e.Ry);
                var el = new Ellipse
                {
                    Width = Math.Max(1, 2 * e.Rx),
                    Height = Math.Max(1, 2 * e.Ry),
                    Stroke = PreviewLine,
                    StrokeThickness = stroke,
                    Fill = e.Fill == SymbolConverter.FillKind.None ? null : e.Fill == SymbolConverter.FillKind.Solid ? PreviewLine : PreviewBodyFill,
                };
                Canvas.SetLeft(el, tl.X);
                Canvas.SetTop(el, tl.Y);
                c.Children.Add(el);
            }

            foreach (var poly in g.Polys)
            {
                var pts = new PointCollection(poly.Points.Select(p => P(p.X, p.Y)));
                Shape shape = poly.Closed
                    ? new Polygon { Points = pts, Fill = poly.Fill == SymbolConverter.FillKind.None ? null : poly.Fill == SymbolConverter.FillKind.Solid ? PreviewLine : PreviewBodyFill }
                    : new Polyline { Points = pts };
                shape.Stroke = poly.Closed && poly.Fill == SymbolConverter.FillKind.Body ? PreviewBody : PreviewLine;
                shape.StrokeThickness = stroke;
                shape.StrokeLineJoin = PenLineJoin.Round;
                c.Children.Add(shape);
            }

            foreach (var a in g.Arcs)
            {
                // Altium arcs go counter-clockwise with Y up; on the Y-down canvas that is clockwise.
                double sweep = a.EndAngle - a.StartAngle;
                if (sweep <= 0) sweep += 360;
                Point At(double deg) => P(a.Cx + a.Rx * Math.Cos(deg * Math.PI / 180), a.Cy + a.Ry * Math.Sin(deg * Math.PI / 180));
                var figure = new PathFigure { StartPoint = At(a.StartAngle) };
                if (sweep >= 359.9)
                {
                    figure.Segments.Add(new ArcSegment(At(a.StartAngle + 180), new Size(a.Rx, a.Ry), 0, false, SweepDirection.Counterclockwise, true));
                    figure.Segments.Add(new ArcSegment(At(a.StartAngle), new Size(a.Rx, a.Ry), 0, false, SweepDirection.Counterclockwise, true));
                }
                else
                {
                    figure.Segments.Add(new ArcSegment(At(a.EndAngle), new Size(a.Rx, a.Ry), 0, sweep > 180, SweepDirection.Counterclockwise, true));
                }
                c.Children.Add(new Path { Data = new PathGeometry(new[] { figure }), Stroke = PreviewLine, StrokeThickness = stroke });
            }

            foreach (var pin in g.Pins)
                DrawPreviewPin(c, pin, P);
        }

        private static void DrawPreviewPin(Canvas c, SymbolConverter.Pin pin, Func<double, double, Point> P)
        {
            var (dx, dy) = SymbolConverter.Direction(pin.Orientation);
            var body = P(pin.X, pin.Y);
            var tip = P(pin.X + dx * pin.Length, pin.Y + dy * pin.Length);
            c.Children.Add(new Line { X1 = body.X, Y1 = body.Y, X2 = tip.X, Y2 = tip.Y, Stroke = PreviewPin, StrokeThickness = 8, StrokeEndLineCap = PenLineCap.Round });

            if (pin.Dot)
            {
                const double r = 25;
                var centre = P(pin.X + dx * r, pin.Y + dy * r);
                var dot = new Ellipse { Width = 2 * r, Height = 2 * r, Stroke = PreviewPin, StrokeThickness = 6 };
                Canvas.SetLeft(dot, centre.X - r);
                Canvas.SetTop(dot, centre.Y - r);
                c.Children.Add(dot);
            }

            bool vertical = pin.Orientation == 90 || pin.Orientation == 270;

            // Name: inside the body, starting just past the body end (like Altium).
            if (pin.ShowName && !string.IsNullOrWhiteSpace(pin.Name))
            {
                var name = new TextBlock { Text = pin.Name, FontSize = 60, Foreground = PreviewName };
                name.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = name.DesiredSize.Width, h = name.DesiredSize.Height;
                var at = P(pin.X - dx * 40, pin.Y - dy * 40);
                if (!vertical)
                {
                    // pin points +X: body (and the text) is to the left of the anchor
                    Canvas.SetLeft(name, dx > 0 ? at.X - w : at.X);
                    Canvas.SetTop(name, at.Y - h / 2);
                }
                else
                {
                    // Rotated -90 about its top-left, the text runs upward from (Left, Top).
                    name.RenderTransform = new RotateTransform(-90);
                    Canvas.SetLeft(name, at.X - h / 2);
                    Canvas.SetTop(name, dy > 0 ? at.Y + w : at.Y);
                }
                c.Children.Add(name);
            }

            // Number: beside the pin line, near the middle.
            if (pin.ShowDesignator && !string.IsNullOrWhiteSpace(pin.Designator))
            {
                var num = new TextBlock { Text = pin.Designator, FontSize = 50, Foreground = PreviewNumber };
                num.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double w = num.DesiredSize.Width, h = num.DesiredSize.Height;
                var mid = P(pin.X + dx * pin.Length / 2, pin.Y + dy * pin.Length / 2);
                if (vertical)
                {
                    num.RenderTransform = new RotateTransform(-90);
                    Canvas.SetLeft(num, mid.X - h - 4);
                    Canvas.SetTop(num, mid.Y + w / 2);
                }
                else
                {
                    Canvas.SetLeft(num, mid.X - w / 2);
                    Canvas.SetTop(num, mid.Y - h - 2);
                }
                c.Children.Add(num);
            }
        }

        // Altium's default schematic library colours.
        private static readonly int BodyBorder = EESCH.RGB(128, 0, 0);
        private static readonly int BodyFill = EESCH.RGB(255, 255, 176);
        private static readonly int LineBlue = EESCH.RGB(0, 0, 255);

        // Recreates the EasyEDA symbol's own drawing: same pin positions, sides, lengths and
        // graphics, converted to Altium coordinates (see SymbolConverter).
        static public void CreateComponent(ISch_Lib schLib, ISch_Component component, string pcbLibraryPath, string package, SymbolData ee_symbol)
        {
            var g = SymbolConverter.Convert(ee_symbol);
            if (g.Pins.Count == 0)
            {
                CreateGeneratedBox(schLib, component, ee_symbol);
            }
            else
            {
                foreach (var r in g.Rects)
                {
                    EESCH.CreateRectangle(schLib, component, r.X1, r.Y1, r.X2, r.Y2, BodyBorder,
                        r.Fill == SymbolConverter.FillKind.Solid ? BodyBorder : BodyFill, r.Fill != SymbolConverter.FillKind.None);
                }
                foreach (var e in g.Ellipses)
                {
                    EESCH.CreateEllipse(schLib, component, e.Cx, e.Cy, e.Rx, e.Ry, LineBlue,
                        e.Fill == SymbolConverter.FillKind.Solid ? LineBlue : BodyFill, e.Fill != SymbolConverter.FillKind.None);
                }
                foreach (var poly in g.Polys)
                {
                    var flat = poly.Points.SelectMany(p => new[] { p.X, p.Y }).ToArray();
                    if (poly.Closed)
                    {
                        bool body = poly.Fill == SymbolConverter.FillKind.Body;
                        EESCH.CreatePolygon(schLib, component, body ? BodyBorder : LineBlue,
                            body ? BodyFill : LineBlue, poly.Fill != SymbolConverter.FillKind.None, flat);
                    }
                    else
                    {
                        EESCH.CreatePolyline(schLib, component, LineBlue, flat);
                    }
                }
                foreach (var a in g.Arcs)
                    EESCH.CreateArc(schLib, component, a.Cx, a.Cy, a.Rx, a.Ry, a.StartAngle, a.EndAngle, LineBlue);

                foreach (var pin in g.Pins)
                {
                    var orientation = pin.Orientation switch
                    {
                        0 => TRotationBy90.eRotate0,
                        90 => TRotationBy90.eRotate90,
                        180 => TRotationBy90.eRotate180,
                        _ => TRotationBy90.eRotate270,
                    };
                    var schPin = EESCH.CreatePin(schLib, component, pin.X, pin.Y, pin.Designator, pin.Name, orientation,
                        pin.Length, AltiumSymbolPin.FromEEPinType(pin.Type), pin.ShowName, null);
                    if (schPin == null)
                        continue;
                    schPin.SetState_ShowDesignator(pin.ShowDesignator);
                    if (pin.Dot)
                        schPin.SetState_Symbol_OuterEdge(TIeeeSymbol.eDot);
                    if (pin.Clock)
                        schPin.SetState_Symbol_InnerEdge(TIeeeSymbol.eClock);
                }
            }
            EESCH.AssignFootprint(component, pcbLibraryPath, package, "");
            schLib.AddSchComponent(component);
        }

        // Fallback when no pins could be read: a generated box with the pins spread around it.
        private static void CreateGeneratedBox(ISch_Lib schLib, ISch_Component component, SymbolData ee_symbol)
        {
            (var rect, var pins) = SymbolDrawing.LayoutPins(ee_symbol.Shapes);
            EESCH.CreateRectangle(schLib, component, rect.X1, rect.Height - rect.Y1, rect.X2, rect.Height - rect.Y2);
            foreach (var pin in pins)
            {
                EESCH.CreatePin(schLib, component, pin.X, rect.Height - pin.Y, pin.Designator, pin.Name, pin.Orientation, pin.Length, pin.PinType, pin.ShowName, null);
            }
        }
    }
}
