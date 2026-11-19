using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.Geometry;
using Rhino.Geometry.Intersect;
using Rhino.Input.Custom;

namespace AdresseGH
{
  /// <summary>Un résultat de pointage : une donnée géométrique d'une sortie GH.</summary>
  public sealed class Hit
  {
    public IGH_DocumentObject Owner;   // composant ou paramètre flottant (sur le canvas)
    public IGH_Param Param;            // sortie qui porte la donnée
    public GH_Path Path;
    public int Index;
    public GeometryBase Geometry;

    public bool WireHit;                              // touché sur un point, une courbe ou une arête
    public double WirePixels = double.MaxValue;       // écart au curseur (pixels)
    public bool FaceHit;                              // touché sur une face
    public double Ray = double.MaxValue;              // distance à la caméra le long du rayon
    public double PickDepth = double.MinValue;        // profondeur Rhino (grand = proche)

    public string TypeName => Geometry?.GetType().Name ?? "?";

    public int KindRank =>
      Geometry is Rhino.Geometry.Point || Geometry is PointCloud ? 0 : Geometry is Curve ? 1 : 2;

    public string Address => $"{Path}[{Index}]";

    public bool IsFloating => ReferenceEquals(Owner, Param);

    public string OwnerLabel =>
      Owner.NickName == Owner.Name ? Owner.Name : $"{Owner.NickName} ({Owner.Name})";

    public string Source => IsFloating ? Owner.NickName : $"{Owner.NickName} › {Param.NickName}";

    public string ShortText => $"{Address}   {TypeName}   ←  {Source}";
  }

  /// <summary>Recherche des previews Grasshopper sous un point de l'écran.</summary>
  public static class Picker
  {
    public static List<Hit> Pick(GH_Document doc, RhinoView view, System.Drawing.Point screenPt, bool includeHidden)
    {
      var hits = new List<Hit>();
      if (doc == null || view == null || doc.PreviewMode == GH_PreviewMode.Disabled) return hits;

      var vp = view.ActiveViewport;
      if (!vp.GetFrustumLine(screenPt.X, screenPt.Y, out Line ray)) return hits;

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
          if (obj is IGH_ActiveObject ao && ao.Locked) continue;           // désactivé
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

    static void Scan(PickContext pc, Line ray, IGH_DocumentObject owner, IGH_Param param, List<Hit> hits)
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

          var bb = goo.Boundingbox;                                   // tri rapide
          if (!bb.IsValid || !pc.PickFrustumTest(bb, out _)) continue;

          var geo = GH_Convert.ToGeometryBase(goo);                   // null pour plans, vecteurs…
          if (geo == null) continue;

          var h = new Hit { Owner = owner, Param = param, Path = path, Index = i, Geometry = geo };
          Test(pc, ray, geo, h);
          if (h.WireHit || h.FaceHit) hits.Add(h);
        }
      }
    }

    static double RayDist(Line ray, Point3d pt) => (pt - ray.From) * ray.UnitTangent;

    static void Test(PickContext pc, Line ray, GeometryBase g, Hit h)
    {
      switch (g)
      {
        case Rhino.Geometry.Point p:
          if (pc.PickFrustumTest(p.Location, out double dpt, out double pp))
          { Wire(h, pp, RayDist(ray, p.Location)); h.PickDepth = Math.Max(h.PickDepth, dpt); }
          break;
        case PointCloud cloud:
          if (pc.PickFrustumTest(cloud, out int ci, out double dcl, out double pcl) && ci >= 0)
          { Wire(h, pcl, RayDist(ray, cloud[ci].Location)); h.PickDepth = Math.Max(h.PickDepth, dcl); }
          break;
        case Curve c: TestCurve(pc, ray, c, h); break;
        case Mesh m: TestMesh(pc, ray, m, h); break;
        case Brep br: TestBrep(pc, ray, br, h); break;
        case Extrusion ex: TestBrep(pc, ray, ex.ToBrep(), h); break;
        case Surface s: TestBrep(pc, ray, s.ToBrep(), h); break;
        case SubD sd: TestMesh(pc, ray, Mesh.CreateFromSubD(sd, 3), h); break;
      }
    }

    static void TestCurve(PickContext pc, Line ray, Curve c, Hit h)
    {
      var nc = c?.ToNurbsCurve();
      if (nc == null || !pc.PickFrustumTest(nc, out double t, out double depth, out double d)) return;
      var pt = nc.ClosestPoints(new LineCurve(ray), out Point3d onCurve, out _) ? onCurve : nc.PointAt(t);
      Wire(h, d, RayDist(ray, pt));
      h.PickDepth = Math.Max(h.PickDepth, depth);
    }

    static void TestMesh(PickContext pc, Line ray, Mesh m, Hit h)
    {
      if (m == null) return;
      if (!pc.PickFrustumTest(m, PickContext.MeshPickStyle.ShadedModePicking,
            out Point3d pt, out double depth, out _, out PickContext.MeshHitFlag _, out int _))
        return;
      double r = Intersection.MeshRay(m, new Ray3d(ray.From, ray.UnitTangent));
      h.FaceHit = true;
      h.Ray = Math.Min(h.Ray, r >= 0 ? r : RayDist(ray, pt));
      h.PickDepth = Math.Max(h.PickDepth, depth);
    }

    static void TestBrep(PickContext pc, Line ray, Brep br, Hit h)
    {
      if (br == null) return;
      foreach (var e in br.Edges) TestCurve(pc, ray, e, h);
      var meshes = Mesh.CreateFromBrep(br, MeshingParameters.FastRenderMesh);
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
}
