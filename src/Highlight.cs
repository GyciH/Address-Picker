using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Rhino.Display;
using Rhino.Geometry;

namespace AdresseGH
{
  /// <summary>Réglages du surlignage (couleur, épaisseur, remplissage), mémorisés dans les réglages GH.</summary>
  public static class HighlightSettings
  {
    const string KeyColor = "Widget.AdresseGH.HighlightColor";
    const string KeyWidth = "Widget.AdresseGH.HighlightWidth";
    const string KeyFill = "Widget.AdresseGH.HighlightFill";

    public static readonly Color DefaultColor = Color.FromArgb(255, 255, 90, 20);
    public static readonly int[] Widths = { 1, 2, 3, 4, 6, 8, 12 };

    public static Color Color
    {
      get => Instances.Settings.GetValue(KeyColor, DefaultColor);
      set { Instances.Settings.SetValue(KeyColor, Color.FromArgb(255, value)); Changed?.Invoke(); }
    }

    public static int Width
    {
      get => Math.Max(1, Math.Min(20, Instances.Settings.GetValue(KeyWidth, 4)));
      set { Instances.Settings.SetValue(KeyWidth, value); Changed?.Invoke(); }
    }

    public static bool Fill
    {
      get => Instances.Settings.GetValue(KeyFill, true);
      set { Instances.Settings.SetValue(KeyFill, value); Changed?.Invoke(); }
    }

    public static event Action Changed;
  }

  /// <summary>Surligne dans Rhino la géométrie trouvée : contour épais et remplissage translucide.</summary>
  public sealed class HighlightConduit : DisplayConduit
  {
    GeometryBase _geometry;
    readonly List<Curve> _wires = new List<Curve>();
    readonly List<Mesh> _meshes = new List<Mesh>();

    public GeometryBase Geometry
    {
      get => _geometry;
      set
      {
        _geometry = value;
        _wires.Clear();
        _meshes.Clear();
        if (value != null) Prepare(value);
      }
    }

    void Prepare(GeometryBase g)
    {
      switch (g)
      {
        case Curve c: _wires.Add(c); break;
        case Brep b: AddBrep(b); break;
        case Extrusion x: AddBrep(x.ToBrep()); break;
        case Surface s: AddBrep(s.ToBrep()); break;
        case Mesh m:
          _meshes.Add(m);
          var naked = m.GetNakedEdges();
          if (naked != null) foreach (var pl in naked) _wires.Add(pl.ToNurbsCurve());
          break;
        case SubD sd:
          var sm = Mesh.CreateFromSubD(sd, 3);
          if (sm != null) _meshes.Add(sm);
          break;
      }
    }

    void AddBrep(Brep b)
    {
      if (b == null) return;
      foreach (var e in b.Edges) _wires.Add(e.DuplicateCurve());
      var ms = Mesh.CreateFromBrep(b, MeshingParameters.FastRenderMesh);
      if (ms != null) _meshes.AddRange(ms);
    }

    protected override void CalculateBoundingBox(CalculateBoundingBoxEventArgs e)
    {
      if (_geometry != null) e.IncludeBoundingBox(_geometry.GetBoundingBox(false));
    }

    protected override void DrawForeground(DrawEventArgs e)
    {
      if (_geometry == null) return;
      var dp = e.Display;
      var col = HighlightSettings.Color;
      int w = HighlightSettings.Width;

      if (HighlightSettings.Fill && _meshes.Count > 0)
      {
        var mat = new DisplayMaterial(col, 0.55);
        foreach (var m in _meshes) dp.DrawMeshShaded(m, mat);
      }
      switch (_geometry)
      {
        case Rhino.Geometry.Point p:
          dp.DrawPoint(p.Location, PointStyle.RoundSimple, 3 + w, col);
          break;
        case PointCloud pc:
          foreach (var it in pc) dp.DrawPoint(it.Location, PointStyle.RoundSimple, 2 + w, col);
          break;
        case SubD sd:
          dp.DrawSubDWires(sd, col, w);
          break;
      }
      foreach (var c in _wires) dp.DrawCurve(c, col, w);
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
      using (var pen = new Pen(HighlightSettings.Color, Math.Max(2f, HighlightSettings.Width)))
        sender.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
    }
  }
}
