using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace AdresseGH
{
  /// <summary>Icônes vectorielles dessinées en GDI+ (aucun texte, aucune ressource).</summary>
  static class Icons
  {
    static readonly Color Orange = Color.FromArgb(255, 255, 90, 20);

    /// <summary>Icône du plugin et du menu des widgets : réticule orange.</summary>
    public static Bitmap Widget(int size)
    {
      var bmp = new Bitmap(size, size);
      using (var g = Graphics.FromImage(bmp))
      using (var pen = new Pen(Orange, Math.Max(2f, size / 12f)))
      {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float m = size / 2f, r = size * 0.28f;
        g.DrawEllipse(pen, m - r, m - r, 2 * r, 2 * r);
        g.DrawLine(pen, m, 1, m, m - r - 1);
        g.DrawLine(pen, m, m + r + 1, m, size - 1);
        g.DrawLine(pen, 1, m, m - r - 1, m);
        g.DrawLine(pen, m + r + 1, m, size - 1, m);
      }
      return bmp;
    }

    // Repère 24 × 24 → rectangle cible.
    static PointF P(RectangleF r, float x, float y) => new PointF(r.X + x * r.Width / 24f, r.Y + y * r.Height / 24f);
    static float U(RectangleF r) => r.Width / 24f;

    /// <summary>Pointer : flèche classique, cercles concentriques sur la pointe.</summary>
    public static void DrawPick(Graphics g, RectangleF r, Color c, float s)
    {
      float u = U(r);
      var tip = P(r, 6, 5);
      using (var pen = new Pen(c, 1.4f * u))
      {
        foreach (var rad in new[] { 2.6f * u, 5.2f * u })
          g.DrawEllipse(pen, tip.X - rad, tip.Y - rad, 2 * rad, 2 * rad);
      }
      var arrow = new[] { P(r, 6, 5), P(r, 6, 20), P(r, 10, 16.2f), P(r, 13, 22), P(r, 15.6f, 20.8f), P(r, 12.6f, 15), P(r, 18, 15) };
      using (var b = new SolidBrush(c)) g.FillPolygon(b, arrow);
      using (var pen = new Pen(Color.FromArgb(110, 0, 0, 0), 0.8f * u) { LineJoin = LineJoin.Round })
        g.DrawPolygon(pen, arrow);
    }

    /// <summary>Isoler : filet à papillon (manche, cerceau, poche maillée).</summary>
    public static void DrawNet(Graphics g, RectangleF r, Color c, float s)
    {
      float u = U(r);
      var hc = P(r, 15, 8);                 // centre du cerceau
      float hr = 6f * u;
      var hoop = new RectangleF(hc.X - hr, hc.Y - hr, 2 * hr, 2 * hr);

      // Poche : cerceau + pointe qui pend vers le bas.
      using (var bag = new GraphicsPath())
      {
        bag.AddEllipse(hoop);
        using (var tail = new GraphicsPath())
        {
          tail.AddBezier(P(r, 9.6f, 10.5f), P(r, 10.5f, 17), P(r, 15, 21.5f), P(r, 19.5f, 19));
          tail.AddBezier(P(r, 19.5f, 19), P(r, 21.5f, 16), P(r, 21.5f, 12), P(r, 20.6f, 10.2f));
          tail.CloseFigure();
          using (var region = new Region(bag))
          {
            region.Union(tail);
            var old = g.Clip;
            g.SetClip(region, CombineMode.Intersect);
            using (var hatch = new Pen(c, 0.8f * u))
            {
              for (float k = -24; k < 48; k += 3.2f)
              {
                g.DrawLine(hatch, P(r, k, 0), P(r, k + 24, 24));
                g.DrawLine(hatch, P(r, k + 24, 0), P(r, k, 24));
              }
            }
            g.Clip = old;
          }
          using (var pen = new Pen(c, 1f * u)) g.DrawPath(pen, tail);
        }
      }
      using (var pen = new Pen(c, 1.8f * u)) g.DrawEllipse(pen, hoop);
      using (var handle = new Pen(c, 2.4f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        g.DrawLine(handle, P(r, 2.5f, 22), P(r, 10.6f, 12.4f));
    }

    /// <summary>Effacer : gomme bicolore inclinée sur un trait.</summary>
    public static void DrawEraser(Graphics g, RectangleF r, Color c, float s)
    {
      float u = U(r);
      var state = g.Save();
      var center = P(r, 13, 11);
      g.TranslateTransform(center.X, center.Y);
      g.RotateTransform(-40);
      var sleeve = new RectangleF(-1 * u, -4.2f * u, 9.5f * u, 8.4f * u);
      var rubber = new RectangleF(-9 * u, -4.2f * u, 8.6f * u, 8.4f * u);
      using (var b = new SolidBrush(c))
      using (var path = AdresseWidget.RoundRect(sleeve, 1.5f * u))
        g.FillPath(b, path);
      using (var pen = new Pen(c, 1.5f * u))
      using (var path = AdresseWidget.RoundRect(rubber, 1.5f * u))
        g.DrawPath(pen, path);
      g.Restore(state);
      using (var line = new Pen(c, 1.6f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        g.DrawLine(line, P(r, 3, 21.5f), P(r, 21, 21.5f));
    }

    /// <summary>Alerte : triangle orange et point d'exclamation.</summary>
    public static void DrawWarning(Graphics g, RectangleF r, float s)
    {
      float u = U(r);
      var tri = new[] { P(r, 12, 2), P(r, 23, 21.5f), P(r, 1, 21.5f) };
      using (var b = new SolidBrush(Orange)) g.FillPolygon(b, tri);
      using (var pen = new Pen(Color.White, 2.4f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        g.DrawLine(pen, P(r, 12, 8.5f), P(r, 12, 14.5f));
      var dot = P(r, 12, 18.2f);
      using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, dot.X - 1.4f * u, dot.Y - 1.4f * u, 2.8f * u, 2.8f * u);
    }

    /// <summary>Rien trouvé : cercle barré.</summary>
    public static void DrawEmpty(Graphics g, RectangleF r, Color c, float s)
    {
      float u = U(r);
      using var pen = new Pen(c, 2f * u);
      var o = P(r, 12, 12);
      float rad = 8 * u;
      g.DrawEllipse(pen, o.X - rad, o.Y - rad, 2 * rad, 2 * rad);
      g.DrawLine(pen, P(r, 6.3f, 17.7f), P(r, 17.7f, 6.3f));
    }

    /// <summary>Copier : deux feuilles superposées.</summary>
    public static void DrawCopy(Graphics g, RectangleF r, Color c, float s)
    {
      float u = U(r);
      using var pen = new Pen(c, 1.6f * u);
      using (var back = AdresseWidget.RoundRect(new RectangleF(P(r, 4, 3), new SizeF(11 * u, 13 * u)), 1.5f * u))
        g.DrawPath(pen, back);
      var front = new RectangleF(P(r, 9, 8), new SizeF(11 * u, 13 * u));
      using (var path = AdresseWidget.RoundRect(front, 1.5f * u))
      using (var b = new SolidBrush(Color.White))
      {
        g.FillPath(b, path);
        g.DrawPath(pen, path);
      }
    }

    /// <summary>Copié : coche.</summary>
    public static void DrawCheck(Graphics g, RectangleF r, Color c, float s)
    {
      float u = U(r);
      using var pen = new Pen(c, 2.6f * u) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
      g.DrawLines(pen, new[] { P(r, 4, 12.5f), P(r, 9.5f, 18), P(r, 20, 6) });
    }
  }
}
