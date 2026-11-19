// =====================================================================
//  Adresse GH — MAQUETTE v2 (Rhino 8, éditeur de scripts, langage C#)
//
//  Cliquer une géométrie de prévisualisation Grasshopper dans un viewport
//  Rhino et retrouver son adresse : composant + sortie + path {a;b} + index.
//  « Isoler » pose sur le canvas un Tree Item réglé sur cette adresse.
//
//  Utilisation :
//    1. Ouvrir Grasshopper avec la définition.
//    2. Rhino : commande ScriptEditor, nouveau script C#, coller ce fichier,
//       lancer (F5). Une fenêtre « Adresse GH » s'ouvre.
//    3. Bouton « Pointer », puis un clic gauche sur une preview GH.
//       (clic droit ou Échap dans la fenêtre = annuler)
//    4. Bouton « Isoler » pour extraire l'élément choisi dans Grasshopper.
//
//  Si l'éditeur signale que Grasshopper est introuvable, décommenter la
//  ligne #r ci-dessous (chemin d'installation par défaut sous Windows).
// =====================================================================
// #r "C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll"

using System;
using System.Collections.Generic;
using System.Linq;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Input.Custom;
using Rhino.UI;
using Grasshopper;
using Grasshopper.GUI.Canvas;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Eto.Forms;
using Eto.Drawing;

// ---------------------------------------------------------------------
// Point d'entrée : une seule fenêtre à la fois (on ferme la précédente)
// ---------------------------------------------------------------------
const string Key = "AdresseGH.Maquette.Form";
(AppDomain.CurrentDomain.GetData(Key) as Form)?.Close();
var form = new AdresseForm();
form.Owner = RhinoEtoApp.MainWindow;
form.Show();
AppDomain.CurrentDomain.SetData(Key, form);

// ---------------------------------------------------------------------
// Un résultat de pointage
// ---------------------------------------------------------------------
class Hit
{
  public IGH_DocumentObject Owner;   // composant ou paramètre flottant (sur le canvas)
  public IGH_Param Param;            // sortie qui porte la donnée
  public GH_Path Path;
  public int Index;
  public GeometryBase Geometry;

  public bool WireHit;               // touché sur un point, une courbe ou une arête
  public double WirePixels = double.MaxValue;   // écart au curseur (pixels)
  public bool FaceHit;               // touché sur une face (mode ombré)
  public double Ray = double.MaxValue;          // distance depuis la caméra, le long du rayon
  public double PickDepth = double.MinValue;    // profondeur donnée par Rhino (grand = proche)

  public string TypeName => Geometry?.GetType().Name ?? "?";

  // 0 = point, 1 = courbe, 2 = surface/maillage : à égalité, le plus « fin » d'abord.
  public int KindRank =>
    Geometry is Rhino.Geometry.Point || Geometry is PointCloud ? 0 : Geometry is Curve ? 1 : 2;

  public string Address => $"{Path}[{Index}]";

  public string OwnerLabel =>
    Owner.NickName == Owner.Name ? Owner.Name : $"{Owner.NickName} ({Owner.Name})";

  public string Source =>
    ReferenceEquals(Owner, Param) ? Owner.NickName : $"{Owner.NickName} › {Param.NickName}";

  public string ShortText => $"{Address}   {TypeName}   ←  {Source}";

  public string LongText
  {
    get
    {
      var lines = new List<string> { $"Composant : {OwnerLabel}" };
      if (!ReferenceEquals(Owner, Param))
        lines.Add($"Sortie : {Param.NickName} ({Param.Name})");
      lines.Add($"Chemin : {Path}");
      lines.Add($"Index : {Index}");
      lines.Add($"Type : {TypeName}");
      lines.Add($"Adresse : {Address}");
      return string.Join(Environment.NewLine, lines);
    }
  }
}

// ---------------------------------------------------------------------
// Recherche des previews GH sous le point cliqué
// ---------------------------------------------------------------------
static class Picker
{
  public static List<Hit> Pick(RhinoView view, System.Drawing.Point screenPt, bool includeHidden)
  {
    var hits = new List<Hit>();
    var doc = Instances.ActiveCanvas?.Document;
    if (doc == null || doc.PreviewMode == GH_PreviewMode.Disabled) return hits;

    var vp = view.ActiveViewport;
    if (!vp.GetFrustumLine(screenPt.X, screenPt.Y, out Rhino.Geometry.Line ray)) return hits;

    using (var pc = new PickContext())
    {
      pc.View = view;
      pc.PickStyle = PickStyle.PointPick;
      pc.SetPickTransform(vp.GetPickTransform(screenPt));
      pc.PickLine = ray;
      pc.UpdateClippingPlanes();

      bool onlySelected = doc.PreviewFilter == GH_PreviewFilter.Selected;

      foreach (var obj in doc.Objects)
      {
        if (!(obj is IGH_PreviewObject po) || po.Hidden || !po.IsPreviewCapable) continue;
        if (obj is IGH_ActiveObject ao && ao.Locked) continue;           // composant désactivé
        if (onlySelected && !obj.Attributes.Selected) continue;

        if (obj is IGH_Component comp)
          foreach (var p in comp.Params.Output) Scan(pc, ray, obj, p, hits);
        else if (obj is IGH_Param prm && prm.Kind == GH_ParamKind.floating)
          Scan(pc, ray, obj, prm, hits);
      }
    }
    if (hits.Count == 0) return hits;

    // Masquage : on écarte ce qui est derrière la première surface touchée.
    if (!includeHidden)
    {
      double front = hits.Min(h => h.Ray);
      double tol = Math.Max(10 * (RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.01), 0.002 * Math.Abs(front));
      // Sécurité : la face que Rhino juge la plus proche est toujours conservée.
      var nearestFace = hits.Where(h => h.FaceHit).OrderByDescending(h => h.PickDepth).FirstOrDefault();
      hits = hits.Where(h => h.Ray <= front + tol || ReferenceEquals(h, nearestFace)).ToList();
    }

    // Priorité : arêtes/courbes/points proches du curseur, puis les faces, la plus proche d'abord.
    return hits.OrderByDescending(h => h.WireHit)
               .ThenBy(h => h.WireHit ? Math.Round(h.WirePixels, 1) : 0)
               .ThenBy(h => h.KindRank)
               .ThenBy(h => h.Ray)
               .ToList();
  }

  static void Scan(PickContext pc, Rhino.Geometry.Line ray, IGH_DocumentObject owner, IGH_Param param, List<Hit> hits)
  {
    if (param is IGH_PreviewObject pp && !pp.IsPreviewCapable) return;
    var data = param.VolatileData;
    if (data == null || data.IsEmpty) return;

    for (int b = 0; b < data.PathCount; b++)
    {
      var path = data.get_Path(b);
      var branch = data.get_Branch(b);
      for (int i = 0; i < branch.Count; i++)
      {
        if (!(branch[i] is IGH_GeometricGoo goo) || !goo.IsValid) continue;

        // Tri rapide : la boîte englobante est-elle dans le cône de pointage ?
        var bb = goo.Boundingbox;
        if (!bb.IsValid || !pc.PickFrustumTest(bb, out _)) continue;

        var geo = GH_Convert.ToGeometryBase(goo);   // null pour plans, vecteurs…
        if (geo == null) continue;

        var h = new Hit { Owner = owner, Param = param, Path = path, Index = i, Geometry = geo };
        Test(pc, ray, geo, h);
        if (h.WireHit || h.FaceHit) hits.Add(h);
      }
    }
  }

  // Distance d'un point à la caméra, mesurée le long du rayon de pointage.
  static double RayDist(Rhino.Geometry.Line ray, Point3d pt) =>
    (pt - ray.From) * ray.UnitTangent;

  static void Test(PickContext pc, Rhino.Geometry.Line ray, GeometryBase g, Hit h)
  {
    switch (g)
    {
      case Rhino.Geometry.Point p:
        if (pc.PickFrustumTest(p.Location, out _, out double dp)) Wire(h, dp, RayDist(ray, p.Location));
        break;
      case PointCloud cloud:
        if (pc.PickFrustumTest(cloud, out int ci, out _, out double dc) && ci >= 0)
          Wire(h, dc, RayDist(ray, cloud[ci].Location));
        break;
      case Curve c:
        TestCurve(pc, ray, c, h);
        break;
      case Mesh m:
        TestMesh(pc, ray, m, h);
        break;
      case Brep br:
        TestBrep(pc, ray, br, h);
        break;
      case Extrusion ex:
        TestBrep(pc, ray, ex.ToBrep(), h);
        break;
      case Surface s:
        TestBrep(pc, ray, s.ToBrep(), h);
        break;
      case SubD sd:
        TestMesh(pc, ray, Mesh.CreateFromSubD(sd, 3), h);
        break;
      // texte, cotes, hachures… hors maquette
    }
  }

  static void TestCurve(PickContext pc, Rhino.Geometry.Line ray, Curve c, Hit h)
  {
    var nc = c?.ToNurbsCurve();
    if (nc == null || !pc.PickFrustumTest(nc, out double t, out double depth, out double d)) return;
    // Point de la courbe le plus proche du rayon de visée (plus fiable que t).
    var pt = nc.ClosestPoints(new LineCurve(ray), out Point3d onCurve, out _) ? onCurve : nc.PointAt(t);
    Wire(h, d, RayDist(ray, pt));
    h.PickDepth = Math.Max(h.PickDepth, depth);
  }

  static void TestMesh(PickContext pc, Rhino.Geometry.Line ray, Mesh m, Hit h)
  {
    if (m == null) return;
    if (!pc.PickFrustumTest(m, PickContext.MeshPickStyle.ShadedModePicking,
          out Point3d pt, out double depth, out _, out PickContext.MeshHitFlag _, out int _))
      return;
    // Première intersection du rayon avec le maillage = face réellement vue.
    double r = Rhino.Geometry.Intersect.Intersection.MeshRay(m, new Ray3d(ray.From, ray.UnitTangent));
    h.FaceHit = true;
    h.Ray = Math.Min(h.Ray, r >= 0 ? r : RayDist(ray, pt));
    h.PickDepth = Math.Max(h.PickDepth, depth);
  }

  static void TestBrep(PickContext pc, Rhino.Geometry.Line ray, Brep br, Hit h)
  {
    if (br == null) return;
    foreach (var e in br.Edges) TestCurve(pc, ray, e, h);                     // arêtes
    var meshes = Mesh.CreateFromBrep(br, MeshingParameters.FastRenderMesh);  // faces
    if (meshes != null)
      foreach (var m in meshes) TestMesh(pc, ray, m, h);
  }

  static void Wire(Hit h, double pixels, double rayDist)
  {
    h.WireHit = true;
    if (pixels < h.WirePixels) h.WirePixels = pixels;
    h.Ray = Math.Min(h.Ray, rayDist);
  }
}

// ---------------------------------------------------------------------
// « Isoler » : Tree Item réglé sur l'adresse + paramètre Geometry, groupés
// ---------------------------------------------------------------------
static class Isolator
{
  public static string Isolate(Hit h)
  {
    var canvas = Instances.ActiveCanvas;
    var doc = canvas?.Document;
    if (doc == null) return "Aucune définition Grasshopper ouverte.";
    if (!doc.Objects.Contains(h.Owner)) return "Le composant source n'existe plus.";

    var proxy = Instances.ComponentServer.FindObjectByName("Tree Item", true, true);
    if (!(proxy?.CreateInstance() is IGH_Component item) || item.Params.Input.Count < 3)
      return "Composant « Tree Item » introuvable.";

    // Entrées : T (arbre), P (chemin), i (index)
    item.CreateAttributes();
    item.NickName = "Item " + h.Address;
    if (!SetPersistent(item.Params.Input[1], h.Path) || !SetPersistent(item.Params.Input[2], h.Index))
      return "Impossible de régler le chemin ou l'index du Tree Item.";
    item.Params.Input[0].AddSource(h.Param);

    var output = new Param_Geometry { Name = "Isolé", NickName = "Isolé " + h.Address };
    output.CreateAttributes();
    output.AddSource(item.Params.Output[0]);

    // Placement à droite du composant source, à hauteur de la sortie.
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
    return $"Isolé : Tree Item {h.Address} ajouté à droite de « {h.Owner.NickName} ».";
  }

  // Écrit une valeur fixe dans un paramètre d'entrée (comme « Set one … » au clic droit),
  // quel que soit le type exact du paramètre.
  static bool SetPersistent(IGH_Param p, object value)
  {
    var pdProp = p.GetType().GetProperty("PersistentData");
    var pd = pdProp?.GetValue(p);
    if (pd == null) return false;

    var gooType = pd.GetType().GetGenericArguments().FirstOrDefault();
    if (gooType == null || !(Activator.CreateInstance(gooType) is IGH_Goo goo)) return false;
    if (!goo.CastFrom(value) && !goo.CastFrom(value.ToString())) return false;

    pd.GetType().GetMethod("Clear", Type.EmptyTypes)?.Invoke(pd, null);
    pd.GetType().GetMethod("Append", new[] { gooType })?.Invoke(pd, new object[] { goo });
    return true;
  }
}

// ---------------------------------------------------------------------
// Clic dans le viewport : intercepté uniquement quand l'outil est armé
// ---------------------------------------------------------------------
class PickMouse : MouseCallback
{
  public bool Armed;
  public Action<RhinoView, System.Drawing.Point> Picked;
  public Action Cancelled;

  protected override void OnMouseDown(MouseCallbackEventArgs e)
  {
    if (!Armed) return;
    e.Cancel = true;                       // Rhino ne sélectionne rien
    Armed = false;
    if (e.MouseButton == Rhino.UI.MouseButton.Left) Picked?.Invoke(e.View, e.ViewportPoint);
    else Cancelled?.Invoke();
  }
}

// ---------------------------------------------------------------------
// Surlignage de la géométrie trouvée dans Rhino
// ---------------------------------------------------------------------
class HighlightConduit : DisplayConduit
{
  public GeometryBase Geometry;
  static readonly System.Drawing.Color Col = System.Drawing.Color.OrangeRed;

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

// ---------------------------------------------------------------------
// Cadre autour du composant sur le canvas Grasshopper
// ---------------------------------------------------------------------
class CanvasMarker
{
  IGH_DocumentObject _target;
  GH_Canvas _canvas;

  public void Mark(IGH_DocumentObject target)
  {
    Clear();
    var canvas = Instances.ActiveCanvas;
    if (canvas?.Document == null || target == null) return;

    _target = target;
    _canvas = canvas;
    _canvas.CanvasPostPaintObjects += OnPaint;

    canvas.Document.DeselectAll();
    target.Attributes.Selected = true;

    var r = target.Attributes.Bounds;                         // centrer la vue dessus
    canvas.Viewport.MidPoint = new System.Drawing.PointF(r.X + r.Width / 2, r.Y + r.Height / 2);
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
    if (!sender.Document.Objects.Contains(_target)) return;   // objet supprimé
    var r = System.Drawing.RectangleF.Inflate(_target.Attributes.Bounds, 8, 8);
    using (var pen = new System.Drawing.Pen(System.Drawing.Color.OrangeRed, 4f))
      sender.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
  }
}

// ---------------------------------------------------------------------
// La fenêtre (préfigure le widget du .gha)
// ---------------------------------------------------------------------
class AdresseForm : Form
{
  readonly PickMouse _mouse = new PickMouse();
  readonly HighlightConduit _conduit = new HighlightConduit();
  readonly CanvasMarker _marker = new CanvasMarker();
  List<Hit> _hits = new List<Hit>();
  RhinoView _lastView;
  System.Drawing.Point _lastPt;

  readonly Button _pickBtn = new Button { Text = "Pointer" };
  readonly Button _isolateBtn = new Button { Text = "Isoler", Enabled = false };
  readonly CheckBox _hiddenChk = new CheckBox { Text = "Inclure les objets masqués", Checked = false };
  readonly Label _status = new Label { Text = "Prêt.", Wrap = WrapMode.Word };
  readonly ListBox _list = new ListBox { Height = 160 };
  readonly TextArea _address = new TextArea { ReadOnly = true, Height = 110, Wrap = false };

  Hit Current => _list.SelectedIndex >= 0 && _list.SelectedIndex < _hits.Count ? _hits[_list.SelectedIndex] : null;

  public AdresseForm()
  {
    Title = "Adresse GH";
    Resizable = true;
    MinimumSize = new Size(520, 480);
    Padding = 8;

    _pickBtn.Click += (s, e) => Arm();
    _isolateBtn.Click += (s, e) =>
    {
      var h = Current;
      if (h == null) return;
      _status.Text = Isolator.Isolate(h);
    };
    _hiddenChk.CheckedChanged += (s, e) => { if (_lastView != null) RunPick(); };
    _list.SelectedIndexChanged += (s, e) =>
    {
      var h = Current;
      _isolateBtn.Enabled = h != null;
      if (h != null) ShowHit(h);
    };
    KeyDown += (s, e) => { if (e.Key == Eto.Forms.Keys.Escape) Disarm("Annulé."); };

    _mouse.Picked = OnPicked;
    _mouse.Cancelled = () => Disarm("Annulé.");
    _mouse.Enabled = true;
    _conduit.Enabled = true;

    Content = new StackLayout
    {
      Spacing = 6,
      HorizontalContentAlignment = HorizontalAlignment.Stretch,
      Items =
      {
        new TableLayout { Spacing = new Size(6, 0), Rows = { new TableRow(new TableCell(_pickBtn, true), new TableCell(_isolateBtn, true)) } },
        _hiddenChk,
        _status,
        new Label { Text = "Objets sous le pointeur :" },
        _list,
        new Label { Text = "Adresse :" },
        new StackLayoutItem(_address, true)
      }
    };

    Closed += (s, e) =>
    {
      _mouse.Enabled = false;
      _conduit.Enabled = false;
      _marker.Clear();
      RhinoDoc.ActiveDoc?.Views.Redraw();
    };
  }

  void Arm()
  {
    if (Instances.ActiveCanvas?.Document == null)
    {
      _status.Text = "Aucune définition Grasshopper ouverte.";
      return;
    }
    // Le cadre et le surlignage précédents disparaissent au nouveau pointage.
    ClearResult();
    _mouse.Armed = true;
    _pickBtn.Enabled = false;
    _status.Text = "Cliquez une preview Grasshopper dans un viewport (clic droit = annuler).";
  }

  void Disarm(string msg)
  {
    _mouse.Armed = false;
    _pickBtn.Enabled = true;
    _status.Text = msg;
  }

  void OnPicked(RhinoView view, System.Drawing.Point pt)
  {
    _pickBtn.Enabled = true;
    _lastView = view;
    _lastPt = pt;
    RunPick();
  }

  void RunPick()
  {
    ClearResult();
    try
    {
      _hits = Picker.Pick(_lastView, _lastPt, _hiddenChk.Checked == true);
    }
    catch (Exception ex)
    {
      _hits = new List<Hit>();
      _status.Text = "Erreur : " + ex.Message;
      return;
    }

    foreach (var h in _hits) _list.Items.Add(h.ShortText);

    if (_hits.Count == 0)
    {
      _status.Text = "Aucune géométrie Grasshopper sous le pointeur.";
      return;
    }
    _status.Text = _hits.Count == 1
      ? "1 objet trouvé."
      : $"{_hits.Count} objets trouvés, le plus probable est sélectionné.";
    _list.SelectedIndex = 0;   // déclenche ShowHit()
  }

  void ShowHit(Hit h)
  {
    _address.Text = h.LongText;
    _conduit.Geometry = h.Geometry;
    _marker.Mark(h.Owner);
    RhinoDoc.ActiveDoc?.Views.Redraw();
  }

  void ClearResult()
  {
    _hits = new List<Hit>();
    _list.Items.Clear();
    _isolateBtn.Enabled = false;
    _address.Text = "";
    _conduit.Geometry = null;
    _marker.Clear();
    RhinoDoc.ActiveDoc?.Views.Redraw();
  }
}
