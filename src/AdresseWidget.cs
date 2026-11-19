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
  /// Widget fixé dans un angle du canvas Grasshopper, sans texte d'interface :
  /// trois boutons à icônes (pointer, isoler, effacer), la liste des candidats
  /// et l'adresse (clic = copier). Options dans le menu clic droit.
  /// </summary>
  public sealed class AdresseWidget : GH_CanvasWidget_FixedObject
  {
    // ---------- réglages persistants ----------
    const string KeyShow = "Widget.AdresseGH.Show";
    const string KeyCorner = "Widget.AdresseGH.Corner";
    const string KeyCollapsed = "Widget.AdresseGH.Collapsed";
    const string KeyHidden = "Widget.AdresseGH.IncludeHidden";

    static readonly string[] CornerNames = { "Top left", "Top right", "Bottom left", "Bottom right" };
    static readonly SizeF[] CornerRatios = { new SizeF(0, 0), new SizeF(1, 0), new SizeF(0, 1), new SizeF(1, 1) };

    // ---------- état ----------
    readonly HighlightConduit _conduit = new HighlightConduit();
    readonly CanvasMarker _marker = new CanvasMarker();
    List<Hit> _hits = new List<Hit>();
    int _selected = -1;
    int _page;
    bool _picking;
    bool _picked;          // un pointage a eu lieu (pour afficher « rien trouvé »)
    bool _copied;          // l'adresse vient d'être copiée
    string _error;         // erreur éventuelle (affichée en infobulle de l'icône ⚠)
    Rhino.Display.RhinoView _lastView;
    System.Drawing.Point _lastPt;
    GH_Canvas _hookedCanvas;

    // ---------- zones cliquables (coordonnées écran du canvas) ----------
    enum Zone { None, Header, Pick, Isolate, Clear, Row, PrevPage, NextPage, Address, Error }
    readonly List<(Rectangle rect, Zone zone, int arg)> _zones = new List<(Rectangle, Zone, int)>();
    Zone _hover = Zone.None;
    int _hoverArg = -1;

    const int RowsPerPage = 6;
    const int W = 340;

    // ---------- GH_Widget ----------
    public override string Name => "Adresse GH";
    public override string Description => "Pick a Grasshopper preview in Rhino to find its component, path and index.";
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

    static bool CatchBehind
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
        int h = 24;                                       // en-tête
        if (Collapsed) return h;
        h += 6 + 36 + 6;                                  // boutons
        if (_error != null || (_picked && _hits.Count == 0)) h += 26;
        if (_hits.Count > 0)
        {
          int rows = Math.Min(RowsPerPage, _hits.Count - _page * RowsPerPage);
          h += rows * 20 + (_hits.Count > RowsPerPage ? 20 : 0) + 6;
        }
        if (Current != null) h += 66;                     // bloc adresse
        return h + 4;
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
      GH_DocumentObject.Menu_AppendItem(menu, "Catch behind", (s, e) =>
      {
        CatchBehind = !CatchBehind;
        if (_lastView != null && Owner != null) RunPick(Owner);
        Owner?.Refresh();
      }, true, CatchBehind);
      GH_DocumentObject.Menu_AppendSeparator(menu);

      // Surlignage : couleur (nuancier), épaisseur (sous-menu), remplissage.
      var colorItem = GH_DocumentObject.Menu_AppendItem(menu, "Highlight colour");
      GH_DocumentObject.Menu_AppendColourPicker(colorItem.DropDown, HighlightSettings.Color,
        (sender, e) => { HighlightSettings.Color = e.Colour; Refresh(); });
      var widthItem = GH_DocumentObject.Menu_AppendItem(menu, "Highlight width");
      foreach (int wpx in HighlightSettings.Widths)
      {
        int v = wpx;
        GH_DocumentObject.Menu_AppendItem(widthItem.DropDown, $"{v} px", (s, e) => { HighlightSettings.Width = v; Refresh(); },
                                          true, HighlightSettings.Width == v);
      }
      GH_DocumentObject.Menu_AppendItem(menu, "Highlight fill", (s, e) => { HighlightSettings.Fill = !HighlightSettings.Fill; Refresh(); },
                                        true, HighlightSettings.Fill);
      GH_DocumentObject.Menu_AppendItem(menu, "Reset highlight", (s, e) =>
      {
        HighlightSettings.Color = HighlightSettings.DefaultColor;
        HighlightSettings.Width = 4;
        HighlightSettings.Fill = true;
        Refresh();
      });

      GH_DocumentObject.Menu_AppendSeparator(menu);
      for (int i = 0; i < 4; i++)
      {
        int corner = i;
        GH_DocumentObject.Menu_AppendItem(menu, CornerNames[i], (s, e) => { Corner = corner; Owner?.Refresh(); }, true, Corner == i);
      }
    }

    void Refresh()
    {
      RhinoDoc.ActiveDoc?.Views.Redraw();
      Owner?.Refresh();
    }

    // ---------- infobulle (uniquement pour le détail d'une erreur) ----------
    public override bool IsTooltipRegion(PointF canvas_coordinate)
    {
      if (_error == null || Owner == null) return false;
      var pt = System.Drawing.Point.Round(Owner.Viewport.ProjectPoint(canvas_coordinate));
      return ZoneAt(pt).zone == Zone.Error;
    }

    public override void SetupTooltip(PointF canvasPoint, GH_TooltipDisplayEventArgs e)
    {
      e.Title = "Adresse GH";
      e.Text = _error ?? "";
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

    internal static readonly Color Accent = Color.FromArgb(255, 255, 90, 20);
    static readonly Color Back = Color.FromArgb(235, 250, 250, 250);
    static readonly Color Border = Color.FromArgb(255, 120, 120, 120);
    static readonly Color TextCol = Color.FromArgb(255, 30, 30, 30);
    static readonly Color Muted = Color.FromArgb(255, 110, 110, 110);
    static readonly Color HeaderCol = Color.FromArgb(255, 60, 60, 60);

    void Draw(Graphics g, Rectangle f)
    {
      float s = Scale;
      int S(float v) => (int)Math.Round(v * s);
      using var font = new Font("Segoe UI", 8.5f);
      using var mono = new Font("Consolas", 8.5f);
      using var monoBig = new Font("Consolas", 11f, FontStyle.Bold);
      using var textBrush = new SolidBrush(TextCol);
      using var mutedBrush = new SolidBrush(Muted);
      using var accentBrush = new SolidBrush(Accent);
      var left = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
      var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

      // Fond
      using (var path = RoundRect(f, S(6)))
      using (var bg = new SolidBrush(Back))
      using (var pen = new Pen(Border, 1f))
      {
        g.FillPath(bg, path);
        g.DrawPath(pen, path);
      }

      int x = f.X + S(8), w = f.Width - S(16), y = f.Y;

      // En-tête : réticule, nombre de candidats, flèche repli.
      var header = new Rectangle(f.X, y, f.Width, S(24));
      using (var hp = RoundRect(header, S(6)))
      using (var hb = new SolidBrush(HeaderCol))
        g.FillPath(hb, hp);
      DrawReticle(g, new PointF(x + S(8), y + S(12)), S(5), Accent, Math.Max(1.5f, 1.5f * s));
      using (var white = new SolidBrush(Color.White))
      {
        if (_hits.Count > 0)
          g.DrawString(_hits.Count.ToString(), font, white, new RectangleF(x + S(22), y, S(60), S(24)), left);
        DrawChevron(g, new PointF(f.Right - S(16), y + S(12)), S(4), Collapsed, Color.White, 1.6f * s);
      }
      _zones.Add((header, Zone.Header, 0));
      y += S(24);
      if (Collapsed) return;

      // Boutons à icônes
      y += S(6);
      int bw = (w - S(12)) / 3;
      var pickRect = new Rectangle(x, y, bw, S(36));
      var isoRect = new Rectangle(x + bw + S(6), y, bw, S(36));
      var clearRect = new Rectangle(x + 2 * (bw + S(6)), y, w - 2 * (bw + S(6)), S(36));
      DrawButton(g, pickRect, true, _picking, _hover == Zone.Pick, s, Icons.DrawPick);
      DrawButton(g, isoRect, Current != null && !_picking, false, _hover == Zone.Isolate, s, Icons.DrawNet);
      DrawButton(g, clearRect, (_hits.Count > 0 || _picked) && !_picking, false, _hover == Zone.Clear, s, Icons.DrawEraser);
      _zones.Add((pickRect, Zone.Pick, 0));
      _zones.Add((isoRect, Zone.Isolate, 0));
      _zones.Add((clearRect, Zone.Clear, 0));
      y += S(36 + 6);

      // Erreur (⚠, détail en infobulle) ou « rien trouvé » (∅)
      if (_error != null)
      {
        var r = new Rectangle(x, y, w, S(26));
        Icons.DrawWarning(g, new RectangleF(x + w / 2f - S(11), y + S(2), S(22), S(22)), s);
        _zones.Add((r, Zone.Error, 0));
        y += S(26);
      }
      else if (_picked && _hits.Count == 0)
      {
        Icons.DrawEmpty(g, new RectangleF(x + w / 2f - S(11), y + S(2), S(22), S(22)), Muted, s);
        y += S(26);
      }

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
          g.DrawString(h.Address, mono, i == _selected ? accentBrush : textBrush,
                       new RectangleF(row.X + S(4), row.Y, S(112), row.Height), left);
          g.DrawString($"{h.TypeName}  ←  {h.Source}", font, textBrush,
                       new RectangleF(row.X + S(120), row.Y, row.Width - S(124), row.Height), left);
          _zones.Add((row, Zone.Row, i));
          y += S(20);
        }
        if (_hits.Count > RowsPerPage)
        {
          var prev = new Rectangle(x, y, S(24), S(20));
          var next = new Rectangle(x + w - S(24), y, S(24), S(20));
          DrawTriangle(g, prev, true, _page > 0 ? TextCol : Color.FromArgb(255, 200, 200, 200), s);
          DrawTriangle(g, next, false, _page < PageCount - 1 ? TextCol : Color.FromArgb(255, 200, 200, 200), s);
          g.DrawString($"{first + 1}–{last} / {_hits.Count}", font, mutedBrush, new RectangleF(x, y, w, S(20)), center);
          _zones.Add((prev, Zone.PrevPage, 0));
          _zones.Add((next, Zone.NextPage, 0));
          y += S(20);
        }
        y += S(6);
      }

      // Bloc adresse : adresse en gros, source, type ; icône copier à droite.
      var cur = Current;
      if (cur != null)
      {
        var block = new Rectangle(x, y, w, S(62));
        using (var bb = new SolidBrush(_hover == Zone.Address ? Color.FromArgb(255, 255, 240, 228) : Color.White))
        using (var pen = new Pen(Accent, 1f))
        {
          g.FillRectangle(bb, block);
          g.DrawRectangle(pen, block);
        }
        int tx = block.X + S(8), tw = block.Width - S(40);
        g.DrawString(cur.Address, monoBig, accentBrush, new RectangleF(tx, block.Y + S(4), tw, S(22)), left);
        g.DrawString(cur.Source, font, textBrush, new RectangleF(tx, block.Y + S(26), tw, S(16)), left);
        g.DrawString(cur.TypeName, font, mutedBrush, new RectangleF(tx, block.Y + S(42), tw, S(16)), left);
        var iconRect = new RectangleF(block.Right - S(30), block.Y + S(6), S(22), S(22));
        if (_copied) Icons.DrawCheck(g, iconRect, Accent, s);
        else Icons.DrawCopy(g, iconRect, _hover == Zone.Address ? Accent : Muted, s);
        _zones.Add((block, Zone.Address, 0));
      }
    }

    static void DrawButton(Graphics g, Rectangle r, bool enabled, bool active, bool hover, float s,
                           Action<Graphics, RectangleF, Color, float> icon)
    {
      var fill = !enabled ? Color.FromArgb(255, 225, 225, 225)
               : active ? Color.FromArgb(255, 200, 60, 0)
               : hover ? Color.FromArgb(255, 255, 120, 60) : Accent;
      using (var path = RoundRect(r, (int)(4 * s)))
      using (var b = new SolidBrush(fill))
        g.FillPath(b, path);
      if (active)
        using (var path = RoundRect(Rectangle.Inflate(r, -(int)(2 * s), -(int)(2 * s)), (int)(3 * s)))
        using (var pen = new Pen(Color.White, 1.5f * s) { DashStyle = DashStyle.Dash })
          g.DrawPath(pen, path);
      float size = 24 * s;
      var ir = new RectangleF(r.X + (r.Width - size) / 2f, r.Y + (r.Height - size) / 2f, size, size);
      icon(g, ir, enabled ? Color.White : Color.FromArgb(255, 160, 160, 160), s);
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

    static void DrawChevron(Graphics g, PointF c, float r, bool collapsed, Color col, float w)
    {
      using var pen = new Pen(col, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
      if (collapsed) g.DrawLines(pen, new[] { new PointF(c.X - r / 2, c.Y - r), new PointF(c.X + r / 2, c.Y), new PointF(c.X - r / 2, c.Y + r) });
      else g.DrawLines(pen, new[] { new PointF(c.X - r, c.Y - r / 2), new PointF(c.X, c.Y + r / 2), new PointF(c.X + r, c.Y - r / 2) });
    }

    static void DrawTriangle(Graphics g, Rectangle r, bool leftward, Color col, float s)
    {
      float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, a = 4 * s;
      var pts = leftward
        ? new[] { new PointF(cx + a / 2, cy - a), new PointF(cx - a, cy), new PointF(cx + a / 2, cy + a) }
        : new[] { new PointF(cx - a / 2, cy - a), new PointF(cx + a, cy), new PointF(cx - a / 2, cy + a) };
      using var b = new SolidBrush(col);
      g.FillPolygon(b, pts);
    }

    internal static GraphicsPath RoundRect(RectangleF r, float radius)
    {
      var p = new GraphicsPath();
      float d = Math.Max(1, radius * 2);
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
          if (Current != null && !_picking)
          {
            _error = Isolator.Isolate(sender, Current);
            _copied = false;
          }
          break;
        case Zone.Clear:
          if (!_picking)
          {
            ClearResult();
            sender.Document?.DeselectAll();
            _lastView = null;
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
        case Zone.Address:
          if (Current != null)
          {
            try { Clipboard.SetText(Current.Address); _copied = true; _error = null; }
            catch (Exception ex) { _error = ex.Message; }
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
        }
      }
      catch (Exception ex)
      {
        _error = ex.Message;
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
      _picked = true;
      try
      {
        _hits = Picker.Pick(canvas.Document, _lastView, _lastPt, CatchBehind);
      }
      catch (Exception ex)
      {
        _error = ex.Message;
        return;
      }
      if (_hits.Count > 0) Select(canvas, 0);
    }

    void Select(GH_Canvas canvas, int index)
    {
      if (index < 0 || index >= _hits.Count) return;
      _selected = index;
      _copied = false;
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
      _picked = false;
      _copied = false;
      _error = null;
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
    }
  }
}
