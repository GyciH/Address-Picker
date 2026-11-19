using System.Drawing;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace AdresseGH
{
  /// <summary>Surligne dans Rhino la géométrie trouvée.</summary>
  public sealed class HighlightConduit : DisplayConduit
  {
    public GeometryBase Geometry;
    public static readonly Color Col = Color.OrangeRed;

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
      if (Geometry != null) e.IncludeBoundingBox(Geometry.GetBoundingBox(false));
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
      var dp = e.Display;
      switch (Geometry)
      {
        case Rhino.Geometry.Point p: dp.DrawPoint(p.Location, PointStyle.RoundSimple, 6, Col); break;
        case PointCloud pc: foreach (var it in pc) dp.DrawPoint(it.Location, PointStyle.RoundSimple, 4, Col); break;
        case Curve c: dp.DrawCurve(c, Col, 4); break;
        case Mesh m: dp.DrawMeshWires(m, Col); break;
        case Brep b: dp.DrawBrepWires(b, Col); break;
        case Extrusion x: dp.DrawBrepWires(x.ToBrep(), Col); break;
        case Surface s: dp.DrawBrepWires(s.ToBrep(), Col); break;
        case SubD sd: dp.DrawSubDWires(sd, Col, 3f); break;
      }
    }
  }

  /// <summary>Sélectionne, centre et encadre un objet sur le canvas Grasshopper.</summary>
  public sealed class CanvasMarker
  {
    IGH_DocumentObject _target;
    GH_Canvas _canvas;

    public void Mark(GH_Canvas canvas, IGH_DocumentObject target)
    {
      Clear();
      if (canvas?.Document == null || target == null) return;

      _target = target;
      _canvas = canvas;
      _canvas.CanvasPostPaintObjects += OnPaint;

      canvas.Document.DeselectAll();
      target.Attributes.Selected = true;

      var r = target.Attributes.Bounds;
      canvas.Viewport.MidPoint = new PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
      canvas.Refresh();
    }

    public void Clear()
    {
      if (_canvas != null)
      {
        _canvas.CanvasPostPaintObjects -= OnPaint;
        _canvas.Refresh();
      }
      _canvas = null;
      _target = null;
    }

    void OnPaint(GH_Canvas sender)
    {
      if (_target?.Attributes == null || sender.Document == null) return;
      if (!sender.Document.Objects.Contains(_target)) return;
      var r = RectangleF.Inflate(_target.Attributes.Bounds, 8, 8);
      using (var pen = new Pen(HighlightConduit.Col, 4f))
        sender.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }
  }
}
