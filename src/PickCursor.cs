using System;
using System.Drawing;
using DColor = System.Drawing.Color;
using Rhino.Display;
using Rhino.Input.Custom;

namespace AdresseGH
{
  /// <summary>
  /// Saisie d'un clic dans un viewport avec un curseur dédié : réticule orange
  /// (cercle de la zone de pointage, repères, étiquette « GH »), sans accrochage.
  /// </summary>
  public sealed class PickCursor : GetPoint
  {
    static readonly DColor Ring = DColor.FromArgb(255, 255, 90, 20);
    static readonly DColor Halo = DColor.FromArgb(200, 255, 255, 255);

    System.Drawing.Point _mouse;
    Guid _viewportId = Guid.Empty;

    public PickCursor()
    {
      SetCommandPrompt("Adresse GH: pick a Grasshopper preview (Esc to cancel)");
      SetCursor(Rhino.UI.CursorStyle.CrossHair);
      PermitObjectSnap(false);
      PermitOrthoSnap(false);
      PermitConstraintOptions(false);
      PermitElevatorMode(0);
      EnableObjectSnapCursors(false);
      AcceptNothing(true);
    }

    protected override void OnMouseMove(GetPointMouseEventArgs e)
    {
      _mouse = e.WindowPoint;
      _viewportId = e.Viewport?.Id ?? Guid.Empty;
      base.OnMouseMove(e);
    }

    protected override void OnDynamicDraw(GetPointDrawEventArgs e)
    {
      base.OnDynamicDraw(e);
      if (e.Viewport == null || e.Viewport.Id != _viewportId) return;

      var d = e.Display;
      float s = 1f;
      try
      {
        var ctl = System.Windows.Forms.Control.FromHandle(e.Viewport.ParentView.Handle);
        if (ctl != null) s = Math.Max(1f, ctl.DeviceDpi / 96f);
      }
      catch { }
      float x = _mouse.X, y = _mouse.Y;
      float r = 11 * s;

      // Cercle (halo blanc dessous pour rester lisible sur fond sombre ou clair).
      DrawCircle(d, x, y, r, Halo, 4f * s);
      DrawCircle(d, x, y, r, Ring, 2f * s);

      // Quatre repères autour du cercle.
      float a = r + 3 * s, b = r + 10 * s;
      foreach (var (dx, dy) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
      {
        var p1 = new PointF(x + dx * a, y + dy * a);
        var p2 = new PointF(x + dx * b, y + dy * b);
        d.Draw2dLine(p1, p2, Halo, 4f * s);
        d.Draw2dLine(p1, p2, Ring, 2f * s);
      }

      // Étiquette « GH ».
      var tag = new Rectangle((int)(x + r + 6 * s), (int)(y + r + 2 * s), (int)(24 * s), (int)(15 * s));
      d.Draw2dRectangle(tag, Ring, 1, Ring);
      d.Draw2dText("GH", DColor.White,
        new Rhino.Geometry.Point2d(tag.X + tag.Width / 2.0, tag.Y + tag.Height / 2.0), true, (int)(11 * s));
    }

    static void DrawCircle(DisplayPipeline d, float cx, float cy, float r, DColor c, float w)
    {
      const int n = 36;
      var prev = new PointF(cx + r, cy);
      for (int i = 1; i <= n; i++)
      {
        double t = 2 * Math.PI * i / n;
        var p = new PointF(cx + (float)(r * Math.Cos(t)), cy + (float)(r * Math.Sin(t)));
        d.Draw2dLine(prev, p, c, w);
        prev = p;
      }
    }
  }
}
