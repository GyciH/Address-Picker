using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace AdresseGH
{
  /// <summary>« Isoler » : Tree Item réglé sur l'adresse + paramètre Geometry, dans un groupe.</summary>
  public static class Isolator
  {
    public static string Isolate(GH_Canvas canvas, Hit h)
    {
      var doc = canvas?.Document;
      if (doc == null) return "Aucune définition ouverte.";
      if (!doc.Objects.Contains(h.Owner)) return "Le composant source n'existe plus.";

      var proxy = Grasshopper.Instances.ComponentServer.FindObjectByName("Tree Item", true, true);
      if (!(proxy?.CreateInstance() is IGH_Component item) || item.Params.Input.Count < 3)
        return "Composant « Tree Item » introuvable.";

      item.CreateAttributes();
      item.NickName = "Item " + h.Address;
      if (!SetPersistent(item.Params.Input[1], h.Path) || !SetPersistent(item.Params.Input[2], h.Index))
        return "Impossible de régler le chemin ou l'index.";
      item.Params.Input[0].AddSource(h.Param);

      var output = new Param_Geometry { Name = "Isolé", NickName = "Isolé " + h.Address };
      output.CreateAttributes();
      output.AddSource(item.Params.Output[0]);

      var srcBox = h.Owner.Attributes.Bounds;
      var outBox = h.Param.Attributes?.Bounds ?? srcBox;
      float y = outBox.Y + outBox.Height / 2;
      item.Attributes.Pivot = new System.Drawing.PointF(srcBox.Right + 90, y);
      item.Attributes.ExpireLayout();
      item.Attributes.PerformLayout();
      output.Attributes.Pivot = new System.Drawing.PointF(item.Attributes.Bounds.Right + 70, y);
      output.Attributes.ExpireLayout();
      output.Attributes.PerformLayout();

      doc.AddObject(item, false, doc.ObjectCount);
      doc.AddObject(output, false, doc.ObjectCount);

      var group = new GH_Group
      {
        NickName = "Isolé " + h.Address,
        Colour = System.Drawing.Color.FromArgb(120, 255, 120, 40)
      };
      group.CreateAttributes();
      group.AddObject(item.InstanceGuid);
      group.AddObject(output.InstanceGuid);
      doc.AddObject(group, false, doc.ObjectCount);

      doc.UndoUtil.RecordAddObjectEvent("Isoler " + h.Address, new List<IGH_DocumentObject> { item, output, group });

      doc.DeselectAll();
      item.Attributes.Selected = true;
      output.Attributes.Selected = true;
      doc.NewSolution(false);
      canvas.Refresh();
      return $"Isolé : Tree Item {h.Address} ajouté.";
    }

    /// <summary>Écrit une valeur fixe dans une entrée, quel que soit le type exact du paramètre.</summary>
    static bool SetPersistent(IGH_Param p, object value)
    {
      var pd = p.GetType().GetProperty("PersistentData")?.GetValue(p);
      if (pd == null) return false;

      var gooType = pd.GetType().GetGenericArguments().FirstOrDefault();
      if (gooType == null || !(Activator.CreateInstance(gooType) is IGH_Goo goo)) return false;
      if (!goo.CastFrom(value) && !goo.CastFrom(value.ToString())) return false;

      pd.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(pd, null);
      pd.GetType().GetMethod("Append", new[] { gooType })?.Invoke(pd, new object[] { goo });
      return true;
    }
  }
}
