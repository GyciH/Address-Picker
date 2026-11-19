using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI;
using Grasshopper.GUI.Canvas;
using Grasshopper.GUI.Widgets;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Input;

namespace AdresseGH
{
  /// <summary>
  /// Widget fixé dans un angle du canvas Grasshopper : Pointer, liste des candidats,
  /// adresse (clic = copier) et Isoler. Visible/masquable via Display › Canvas Widgets.
  /// </summary>
  public sealed class AdresseWidget : GH_CanvasWidget_FixedObject
  {
    // ---------- réglages persistants ----------
    const string KeyShow = "Widget.AdresseGH.Show";
    const string KeyCorner = "Widget.AdresseGH.Corner";
    const string KeyCollapsed = "Widget.AdresseGH.Collapsed";
    const string KeyHidden = "Widget.AdresseGH.IncludeHidden";

    static readonly string[] CornerNames = { "En haut à gauche", "En haut à droite", "En bas à gauche", "En bas à droite" };
    static readonly SizeF[] CornerRatios = { new SizeF(0, 0), new SizeF(1, 0), new SizeF(0, 1), new SizeF(1, 1) };

    // ---------- état ----------
    readonly HighlightConduit _conduit = new HighlightConduit();
    readonly CanvasMarker _marker = new CanvasMarker();
    List<Hit> _hits = new List<Hit>();
    int _selected = -1;
    int _page;
    bool _picking;
    string _status = "Cliquez « Pointer », puis une géométrie dans Rhino.";
    Rhino.Display.RhinoView _lastView;
    System.Drawing.Point _lastPt;
    GH_Canvas _hookedCanvas;

    // ---------- zones cliquables (coordonnées écran du canvas) ----------
    enum Zone { None, Header, Pick, Isolate, Clear, Row, PrevPage, NextPage, Address, Hidden }
    readonly List<(Rectangle rect, Zone zone, int arg)> _zones = new List<(Rectangle, Zone, int)>();
    Zone _hover = Zone.None;
    int _hoverArg = -1;

    const int RowsPerPage = 6;
    const int W = 360;

    // ---------- GH_Widget ----------
    public override string Name => "Adresse GH";
    public override string Description => "Retrouver le composant, le chemin et l'index d'une preview cliquée dans Rhino.";
    public override Bitmap Icon_24x24 => Icons.Widget(24);

    public override bool Visible
    {
      get => Instances.Settings.GetValue(KeyShow, true);
      set
      {
        Instances.Settings.SetValue(KeyShow, value);
        if (!value) ClearResult();
      }
    }

    static bool Collapsed
    {
      get => Instances.Settings.GetValue(KeyCollapsed, false);
      set => Instances.Settings.SetValue(KeyCollapsed, value);
    }

    static bool IncludeHidden
    {
      get => Instances.Settings.GetValue(KeyHidden, false);
      set => Instances.Settings.SetValue(KeyHidden, value);
    }

    static int Corner
    {
      get => Math.Max(0, Math.Min(3, Instances.Settings.GetValue(KeyCorner, 1)));
      set => Instances.Settings.SetValue(KeyCorner, value);
    }

    public override SizeF Ratio
    {
      get => CornerRatios[Corner];
      set { }
    }

    public override int Padding => 10;

    float Scale => Owner != null ? Math.Max(1f, Owner.DeviceDpi / 96f) : 1f;

    Hit Current => _selected >= 0 && _selected < _hits.Count ? _hits[_selected] : null;

    int PageCount => Math.Max(1, (_hits.Count + RowsPerPage - 1) / RowsPerPage);

    // Hauteur (non mise à l'échelle) selon l'état.
    int LogicalHeight
    {
      get
      {
        int h = 26;                                     // en-tête
        if (Collapsed) return h;
        h += 6 + 26 + 6 + 18 + 34;                      // boutons, case « masqués », statut
        if (_hits.Count > 0)
        {
          int rows = Math.Min(RowsPerPage, _hits.Count - _page * RowsPerPage);
          h += rows * 20 + (_hits.Count > RowsPerPage ? 20 : 0) + 6;
        }
        if (Current != null) h += 6 * 16 + 14;          // bloc adresse
        return h + 8;
      }
    }

    public override Size Size => new Size((int)(W * Scale), (int)(LogicalHeight * Scale));

    public override bool Contains(System.Drawing.Point pt_control, PointF pt_canvas)
    {
      if (Owner?.Document == null || !Visible) return false;
      return Frame().Contains(pt_control);
    }

    Rectangle Frame()
    {
      var c = ControlLocation(Owner.Viewport);
      var s = Size;
      return new Rectangle(c.X - s.Width / 2, c.Y - s.Height / 2, s.Width, s.Height);
    }

    // ---------- menu clic droit ----------
    public override void AppendToMenu(ToolStripDropDownMenu menu)
    {
      base.AppendToMenu(menu);
      GH_DocumentObject.Menu_AppendSeparator(menu);
      for (int i = 0; i < 4; i++)
      {
        int corner = i;
        GH_DocumentObject.Menu_AppendItem(menu, CornerNames[i], (s, e) => { Corner = corner; Owner?.Refresh(); }, true, Corner == i);
      }
    }

    // ---------- rendu ----------
    protected override void Render_Internal(GH_Canvas canvas, System.Drawing.Point controlAnchor, PointF canvasAnchor,
                                            Rectangle controlFrame, RectangleF canvasFrame)
    {
      if (canvas.Document == null) return;
      HookCanvas(canvas);

      var g = canvas.Graphics;
      var oldTransform = g.Transform;
      var oldSmoothing = g.SmoothingMode;
      var oldText = g.TextRenderingHint;
      g.ResetTransform();
      g.SmoothingMode = SmoothingMode.AntiAlias;
      g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
      _zones.Clear();

      try { Draw(g, controlFrame); }
      finally
      {
        g.Transform = oldTransform;
        g.SmoothingMode = oldSmoothing;
        g.TextRenderingHint = oldText;
      }
    }

    static readonly Color Accent = Color.FromArgb(255, 255, 90, 20);
    static readonly Color Back = Color.FromArgb(235, 250, 250, 250);
    static readonly Color Border = Color.FromArgb(255, 120, 120, 120);
    static readonly Color TextCol = Color.FromArgb(255, 30, 30, 30);
    static readonly Color Muted = Color.FromArgb(255, 110, 110, 110);

    void Draw(Graphics g, Rectangle f)
    {
      float s = Scale;
      int S(float v) => (int)Math.Round(v * s);
      using var font = new Font("Segoe UI", 8.5f);
      using var bold = new Font("Segoe UI", 8.5f, FontStyle.Bold);
      using var mono = new Font("Consolas", 8.5f);
      using var textBrush = new SolidBrush(TextCol);
      using var mutedBrush = new SolidBrush(Muted);
      using var accentBrush = new SolidBrush(Accent);
      var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
      var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
      var wrap = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisWord };

      // Fond
      using (var path = RoundRect(f, S(6)))
      using (var bg = new SolidBrush(Back))
      using (var pen = new Pen(Border, 1f))
      {
        g.FillPath(bg, path);
        g.DrawPath(pen, path);
      }

      int x = f.X + S(8), w = f.Width - S(16), y = f.Y;

      // En-tête
      var header = new Rectangle(f.X, y, f.Width, S(26));
      using (var hp = RoundRect(header, S(6)))
      using (var hb = new SolidBrush(Color.FromArgb(255, 60, 60, 60)))
        g.FillPath(hb, hp);
      DrawReticle(g, new PointF(x + S(8), y + S(13)), S(5), Accent, Math.Max(1.5f, 1.5f * s));
      using (var white = new SolidBrush(Color.White))
      {
        g.DrawString("Adresse GH", bold, white, new RectangleF(x + S(20), y, w - S(40), S(26)), left);
        g.DrawString(Collapsed ? "▸" : "▾", bold, white, new RectangleF(f.Right - S(26), y, S(20), S(26)), center);
      }
      _zones.Add((header, Zone.Header, 0));
      y += S(26);
      if (Collapsed) return;

      // Boutons
      y += S(6);
      int bw = (w - S(12)) / 3;
      var pickRect = new Rectangle(x, y, bw, S(26));
      var isoRect = new Rectangle(x + bw + S(6), y, bw, S(26));
      var clearRect = new Rectangle(x + 2 * (bw + S(6)), y, w - 2 * (bw + S(6)), S(26));
      DrawButton(g, pickRect, _picking ? "Cliquez…" : "Pointer", true, _hover == Zone.Pick, bold, s);
      DrawButton(g, isoRect, "Isoler", Current != null && !_picking, _hover == Zone.Isolate, bold, s);
      DrawButton(g, clearRect, "Effacer", _hits.Count > 0 && !_picking, _hover == Zone.Clear, bold, s);
      _zones.Add((pickRect, Zone.Pick, 0));
      _zones.Add((isoRect, Zone.Isolate, 0));
      _zones.Add((clearRect, Zone.Clear, 0));
      y += S(26 + 6);

      // Case « objets masqués »
      var chk = new Rectangle(x, y + S(3), S(12), S(12));
      using (var pen = new Pen(Border, 1f)) g.DrawRectangle(pen, chk);
      if (IncludeHidden)
        using (var pen = new Pen(Accent, 2f * s))
          g.DrawLines(pen, new[] { new PointF(chk.X + S(2), chk.Y + S(6)), new PointF(chk.X + S(5), chk.Y + S(10)), new PointF(chk.Right - S(2), chk.Y + S(2)) });
      g.DrawString("Inclure les objets masqués", font, textBrush, new RectangleF(chk.Right + S(6), y, w - S(20), S(18)), left);
      _zones.Add((new Rectangle(x, y, w, S(18)), Zone.Hidden, 0));
      y += S(18);

      // Statut
      g.DrawString(_status, font, mutedBrush, new RectangleF(x, y + S(2), w, S(32)), wrap);
      y += S(34);

      // Liste des candidats
      if (_hits.Count > 0)
      {
        int first = _page * RowsPerPage;
        int last = Math.Min(_hits.Count, first + RowsPerPage);
        for (int i = first; i < last; i++)
        {
          var row = new Rectangle(x, y, w, S(20));
          if (i == _selected)
            using (var sb = new SolidBrush(Color.FromArgb(60, Accent))) g.FillRectangle(sb, row);
          else if (_hover == Zone.Row && _hoverArg == i)
            using (var hb = new SolidBrush(Color.FromArgb(25, 0, 0, 0))) g.FillRectangle(hb, row);
          var h = _hits[i];
          var addrRect = new RectangleF(row.X + S(4), row.Y, S(120), row.Height);
          g.DrawString(h.Address, mono, i == _selected ? accentBrush : textBrush, addrRect, left);
          g.DrawString($"{h.TypeName}  ←  {h.Source}", font, textBrush,
                       new RectangleF(row.X + S(128), row.Y, row.Width - S(132), row.Height), left);
          _zones.Add((row, Zone.Row, i));
          y += S(20);
        }
        if (_hits.Count > RowsPerPage)
        {
          var prev = new Rectangle(x, y, S(24), S(20));
          var next = new Rectangle(x + w - S(24), y, S(24), S(20));
          g.DrawString("◀", font, _page > 0 ? textBrush : mutedBrush, prev, center);
          g.DrawString("▶", font, _page < PageCount - 1 ? textBrush : mutedBrush, next, center);
          g.DrawString($"{first + 1}–{last} sur {_hits.Count}", font, mutedBrush, new RectangleF(x, y, w, S(20)), center);
          _zones.Add((prev, Zone.PrevPage, 0));
          _zones.Add((next, Zone.NextPage, 0));
          y += S(20);
        }
        y += S(6);
      }

      // Bloc adresse
      var cur = Current;
      if (cur != null)
      {
        var block = new Rectangle(x, y, w, S(6 * 16 + 8));
        using (var bb = new SolidBrush(_hover == Zone.Address ? Color.FromArgb(255, 255, 240, 228) : Color.White))
        using (var pen = new Pen(Accent, 1f))
        {
          g.FillRectangle(bb, block);
          g.DrawRectangle(pen, block);
        }
        var lines = new List<(string label, string value)>
        {
          ("Composant", cur.OwnerLabel),
          (cur.IsFloating ? "Paramètre" : "Sortie", cur.IsFloating ? cur.Param.Name : $"{cur.Param.NickName} ({cur.Param.Name})"),
          ("Chemin", cur.Path.ToString()),
          ("Index", cur.Index.ToString()),
          ("Type", cur.TypeName),
          ("Adresse", cur.Address),
        };
        int ly = block.Y + S(4);
        foreach (var (label, value) in lines)
        {
          g.DrawString(label, font, mutedBrush, new RectangleF(block.X + S(6), ly, S(76), S(16)), left);
          g.DrawString(value, label == "Adresse" ? bold : font, label == "Adresse" ? accentBrush : textBrush,
                       new RectangleF(block.X + S(84), ly, block.Width - S(90), S(16)), left);
          ly += S(16);
        }
        g.DrawString("clic = copier", font, mutedBrush,
                     new RectangleF(block.X, block.Bottom - S(18), block.Width - S(6), S(16)),
                     new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center });
        _zones.Add((block, Zone.Address, 0));
      }
    }

    static void DrawButton(Graphics g, Rectangle r, string text, bool enabled, bool hover, Font font, float s)
    {
      var fill = !enabled ? Color.FromArgb(255, 225, 225, 225)
               : hover ? Color.FromArgb(255, 255, 120, 60) : Accent;
      using (var path = RoundRect(r, (int)(4 * s)))
      using (var b = new SolidBrush(fill))
        g.FillPath(b, path);
      using (var tb = new SolidBrush(enabled ? Color.White : Color.FromArgb(255, 150, 150, 150)))
        g.DrawString(text, font, tb, r, new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center });
    }

    static void DrawReticle(Graphics g, PointF c, float r, Color col, float w)
    {
      using var pen = new Pen(col, w);
      g.DrawEllipse(pen, c.X - r, c.Y - r, 2 * r, 2 * r);
      g.DrawLine(pen, c.X, c.Y - r - 3, c.X, c.Y - r + 2);
      g.DrawLine(pen, c.X, c.Y + r - 2, c.X, c.Y + r + 3);
      g.DrawLine(pen, c.X - r - 3, c.Y, c.X - r + 2, c.Y);
      g.DrawLine(pen, c.X + r - 2, c.Y, c.X + r + 3, c.Y);
    }

    static GraphicsPath RoundRect(Rectangle r, int radius)
    {
      var p = new GraphicsPath();
      int d = Math.Max(1, radius * 2);
      p.AddArc(r.X, r.Y, d, d, 180, 90);
      p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
      p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
      p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
      p.CloseFigure();
      return p;
    }

    // ---------- souris ----------
    (Zone zone, int arg) ZoneAt(System.Drawing.Point pt)
    {
      for (int i = _zones.Count - 1; i >= 0; i--)
        if (_zones[i].rect.Contains(pt)) return (_zones[i].zone, _zones[i].arg);
      return (Zone.None, -1);
    }

    public override GH_ObjectResponse RespondToMouseMove(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (sender.Document == null) return GH_ObjectResponse.Ignore;
      var (zone, arg) = Contains(e.ControlLocation, e.CanvasLocation) ? ZoneAt(e.ControlLocation) : (Zone.None, -1);
      if (zone != _hover || arg != _hoverArg)
      {
        _hover = zone;
        _hoverArg = arg;
        sender.Refresh();
      }
      return zone == Zone.None ? GH_ObjectResponse.Ignore : GH_ObjectResponse.Handled;
    }

    public override GH_ObjectResponse RespondToMouseDown(GH_Canvas sender, GH_CanvasMouseEvent e)
    {
      if (sender.Document == null || !Contains(e.ControlLocation, e.CanvasLocation))
        return GH_ObjectResponse.Ignore;
      if (e.Button != MouseButtons.Left) return base.RespondToMouseDown(sender, e);

      var (zone, arg) = ZoneAt(e.ControlLocation);
      switch (zone)
      {
        case Zone.Header:
          Collapsed = !Collapsed;
          break;
        case Zone.Pick:
          if (!_picking) sender.BeginInvoke(new Action(() => StartPick(sender)));
          break;
        case Zone.Isolate:
          if (Current != null && !_picking) _status = Isolator.Isolate(sender, Current);
          break;
        case Zone.Clear:
          if (!_picking && _hits.Count > 0)
          {
            ClearResult();
            if (sender.Document != null) { sender.Document.DeselectAll(); }
            _lastView = null;
            _status = "Sélection effacée.";
          }
          break;
        case Zone.Row:
          Select(sender, arg);
          break;
        case Zone.PrevPage:
          if (_page > 0) _page--;
          break;
        case Zone.NextPage:
          if (_page < PageCount - 1) _page++;
          break;
        case Zone.Hidden:
          IncludeHidden = !IncludeHidden;
          if (_lastView != null) RunPick(sender);
          break;
        case Zone.Address:
          if (Current != null)
          {
            try { Clipboard.SetText(Current.Address); _status = $"Adresse {Current.Address} copiée."; }
            catch { _status = "Impossible de copier l'adresse."; }
          }
          break;
      }
      sender.Refresh();
      return GH_ObjectResponse.Handled;
    }

    // ---------- logique ----------
    void StartPick(GH_Canvas canvas)
    {
      if (canvas.Document == null) return;
      ClearResult();
      _picking = true;
      _status = "Cliquez une preview Grasshopper dans un viewport Rhino (Échap = annuler).";
      canvas.Refresh();

      try
      {
        using (var gp = new PickCursor())
        {
          var res = gp.Get();
          if (res == GetResult.Point && gp.View() != null)
          {
            _lastView = gp.View();
            _lastPt = gp.Point2d();
            _picking = false;
            RunPick(canvas);
          }
          else _status = "Pointage annulé.";
        }
      }
      catch (Exception ex)
      {
        _status = "Erreur : " + ex.Message;
      }
      finally
      {
        _picking = false;
        canvas.Refresh();
      }
    }

    void RunPick(GH_Canvas canvas)
    {
      ClearResult();
      try
      {
        _hits = Picker.Pick(canvas.Document, _lastView, _lastPt, IncludeHidden);
      }
      catch (Exception ex)
      {
        _status = "Erreur : " + ex.Message;
        return;
      }

      if (_hits.Count == 0)
      {
        _status = "Aucune géométrie Grasshopper sous le pointeur.";
        return;
      }
      _status = _hits.Count == 1 ? "1 objet trouvé."
              : $"{_hits.Count} objets trouvés, le plus probable est sélectionné.";
      Select(canvas, 0);
    }

    void Select(GH_Canvas canvas, int index)
    {
      if (index < 0 || index >= _hits.Count) return;
      _selected = index;
      var h = _hits[index];
      _conduit.Geometry = h.Geometry;
      _conduit.Enabled = true;
      _marker.Mark(canvas, h.Owner);
      RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    void ClearResult()
    {
      _hits = new List<Hit>();
      _selected = -1;
      _page = 0;
      _conduit.Geometry = null;
      _conduit.Enabled = false;
      _marker.Clear();
      RhinoDoc.ActiveDoc?.Views.Redraw();
    }

    void HookCanvas(GH_Canvas canvas)
    {
      if (ReferenceEquals(_hookedCanvas, canvas)) return;
      if (_hookedCanvas != null)
      {
        _hookedCanvas.DocumentChanged -= OnDocumentChanged;
        _hookedCanvas.MouseMove -= OnCanvasMouseMove;
        _hookedCanvas.MouseLeave -= OnCanvasMouseLeave;
      }
      _hookedCanvas = canvas;
      canvas.DocumentChanged += OnDocumentChanged;
      canvas.MouseMove += OnCanvasMouseMove;
      canvas.MouseLeave += OnCanvasMouseLeave;
    }

    // Le canvas n'appelle le widget que si la souris est dessus : on efface le survol en sortant.
    void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
      if (_hover != Zone.None && Owner?.Document != null && !Frame().Contains(e.Location)) ClearHover();
    }

    void OnCanvasMouseLeave(object sender, EventArgs e)
    {
      if (_hover != Zone.None) ClearHover();
    }

    void ClearHover()
    {
      _hover = Zone.None;
      _hoverArg = -1;
      Owner?.Refresh();
    }

    void OnDocumentChanged(GH_Canvas sender, GH_CanvasDocumentChangedEventArgs e)
    {
      ClearResult();
      _status = "Cliquez « Pointer », puis une géométrie dans Rhino.";
    }
  }
}
