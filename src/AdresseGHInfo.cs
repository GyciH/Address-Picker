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
      "Pick a Grasshopper preview in Rhino to find its component, data tree path and index, then isolate it.";
    public override Guid Id => new Guid("6f0c7f3e-3b1e-4a3a-9b8e-2f6c1d9a4e21");
    public override string AuthorName => "Jérémy Carolus";
    public override string AuthorContact => "https://github.com/GyciH/Address-Picker";
    public override string Version => "1.1.0";
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
}
