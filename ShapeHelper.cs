using System;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;

namespace Scribo;

public static class ShapeHelper
{
    public static Stroke CreateArrowStroke(Point start, Point end, DrawingAttributes attributes)
    {
        var points = new StylusPointCollection();
        points.Add(new StylusPoint(start.X, start.Y));
        points.Add(new StylusPoint(end.X, end.Y));

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double angle = Math.Atan2(dy, dx);
        double headLength = Math.Max(16.0, attributes.Width * 3.5);

        double arrowAngle = Math.PI / 6.0; // 30 degrees

        Point leftWing = new Point(
            end.X - headLength * Math.Cos(angle - arrowAngle),
            end.Y - headLength * Math.Sin(angle - arrowAngle));

        Point rightWing = new Point(
            end.X - headLength * Math.Cos(angle + arrowAngle),
            end.Y - headLength * Math.Sin(angle + arrowAngle));

        // Path: end -> leftWing -> end -> rightWing
        points.Add(new StylusPoint(leftWing.X, leftWing.Y));
        points.Add(new StylusPoint(end.X, end.Y));
        points.Add(new StylusPoint(rightWing.X, rightWing.Y));

        var shapeDa = attributes.Clone();
        shapeDa.FitToCurve = false;

        return new Stroke(points, shapeDa);
    }

    public static Stroke CreateRectangleStroke(Point start, Point end, DrawingAttributes attributes)
    {
        double left = Math.Min(start.X, end.X);
        double top = Math.Min(start.Y, end.Y);
        double right = Math.Max(start.X, end.X);
        double bottom = Math.Max(start.Y, end.Y);

        var points = new StylusPointCollection
        {
            new StylusPoint(left, top),
            new StylusPoint(right, top),
            new StylusPoint(right, bottom),
            new StylusPoint(left, bottom),
            new StylusPoint(left, top)
        };

        var shapeDa = attributes.Clone();
        shapeDa.FitToCurve = false;

        return new Stroke(points, shapeDa);
    }

    public static Stroke CreateEllipseStroke(Point start, Point end, DrawingAttributes attributes)
    {
        var points = new StylusPointCollection();
        double cx = (start.X + end.X) / 2.0;
        double cy = (start.Y + end.Y) / 2.0;
        double rx = Math.Abs(end.X - start.X) / 2.0;
        double ry = Math.Abs(end.Y - start.Y) / 2.0;

        int segments = 48;
        for (int i = 0; i <= segments; i++)
        {
            double theta = (i / (double)segments) * 2.0 * Math.PI;
            double x = cx + rx * Math.Cos(theta);
            double y = cy + ry * Math.Sin(theta);
            points.Add(new StylusPoint(x, y));
        }

        var shapeDa = attributes.Clone();
        shapeDa.FitToCurve = false;

        return new Stroke(points, shapeDa);
    }

    public static Stroke CreateLineStroke(Point start, Point end, DrawingAttributes attributes)
    {
        var points = new StylusPointCollection
        {
            new StylusPoint(start.X, start.Y),
            new StylusPoint(end.X, end.Y)
        };

        var shapeDa = attributes.Clone();
        shapeDa.FitToCurve = false;

        return new Stroke(points, shapeDa);
    }

    public static Stroke CreateDoubleArrowStroke(Point start, Point end, DrawingAttributes attributes)
    {
        var points = new StylusPointCollection();

        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double angle = Math.Atan2(dy, dx);
        double headLength = Math.Max(16.0, attributes.Width * 3.5);
        double arrowAngle = Math.PI / 6.0; // 30 degrees

        // Start arrowhead (pointing towards start)
        Point startLeft = new Point(
            start.X + headLength * Math.Cos(angle - arrowAngle),
            start.Y + headLength * Math.Sin(angle - arrowAngle));
        Point startRight = new Point(
            start.X + headLength * Math.Cos(angle + arrowAngle),
            start.Y + headLength * Math.Sin(angle + arrowAngle));

        points.Add(new StylusPoint(startLeft.X, startLeft.Y));
        points.Add(new StylusPoint(start.X, start.Y));
        points.Add(new StylusPoint(startRight.X, startRight.Y));
        points.Add(new StylusPoint(start.X, start.Y));

        // Shaft to end
        points.Add(new StylusPoint(end.X, end.Y));

        // End arrowhead (pointing towards end)
        Point endLeft = new Point(
            end.X - headLength * Math.Cos(angle - arrowAngle),
            end.Y - headLength * Math.Sin(angle - arrowAngle));
        Point endRight = new Point(
            end.X - headLength * Math.Cos(angle + arrowAngle),
            end.Y - headLength * Math.Sin(angle + arrowAngle));

        points.Add(new StylusPoint(endLeft.X, endLeft.Y));
        points.Add(new StylusPoint(end.X, end.Y));
        points.Add(new StylusPoint(endRight.X, endRight.Y));

        var shapeDa = attributes.Clone();
        shapeDa.FitToCurve = false;

        return new Stroke(points, shapeDa);
    }
}
