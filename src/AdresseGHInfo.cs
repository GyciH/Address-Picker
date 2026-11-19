using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace AdresseGH
{
  public sealed class AdresseGHInfo : GH_AssemblyInfo
  {
    public override string Name => "Adresse GH";
    public override string Description =>
      "Cliquez une preview Grasshopper dans Rhino pour retrouver son composant, son chemin et son index, puis l'isoler.";
    public override Guid Id => new Guid("6f0c7f3e-3b1e-4a3a-9b8e-2f6c1d9a4e21");
    public override string AuthorName => "Jérémy Carolus";
    public override string AuthorContact => "https://github.com/GyciH/Address-Picker";
    public override string Version => "1.0.1";
    public override Bitmap Icon => Icons.Widget(24);
  }

  /// <summary>Ajoute le widget à chaque canvas Grasshopper créé.</summary>
  public sealed class AdresseGHLoader : GH_AssemblyPriority
  {
    public override GH_LoadingInstruction PriorityLoad()
    {
      Instances.CanvasCreated += AddWidget;
      if (Instances.ActiveCanvas != null) AddWidget(Instances.ActiveCanvas);
      return GH_LoadingInstruction.Proceed;
    }

    static void AddWidget(Grasshopper.GUI.Canvas.GH_Canvas canvas)
    {
      if (canvas == null) return;
      foreach (var w in canvas.Widgets) if (w is AdresseWidget) return;
      var widget = new AdresseWidget { Owner = canvas };
      canvas.Widgets.Add(widget);
    }
  }

  static class Icons
  {
    /// <summary>Icône dessinée : réticule orange.</summary>
    public static Bitmap Widget(int size)
    {
      var bmp = new Bitmap(size, size);
      using (var g = Graphics.FromImage(bmp))
      using (var pen = new Pen(Color.FromArgb(255, 255, 90, 20), Math.Max(2f, size / 12f)))
      {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        float m = size / 2f, r = size * 0.28f;
        g.DrawEllipse(pen, m - r, m - r, 2 * r, 2 * r);
        g.DrawLine(pen, m, 1, m, m - r - 1);
        g.DrawLine(pen, m, m + r + 1, m, size - 1);
        g.DrawLine(pen, 1, m, m - r - 1, m);
        g.DrawLine(pen, m + r + 1, m, size - 1, m);
      }
      return bmp;
    }
  }
}
