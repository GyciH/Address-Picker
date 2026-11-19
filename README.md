# Address-Picker (Adresse GH)

<img src="docs/food4rhino/icon_256.png" alt="Adresse GH icon" width="96" align="right">

Click a Grasshopper preview in a Rhino viewport to find its source component, data tree path and index, then isolate it in one click.

*Cliquez une preview Grasshopper dans Rhino pour retrouver son composant, son chemin et son index, puis isolez-la en un clic.* ([Français plus bas](#français))

**Rhino 8 for Windows** · Grasshopper plug-in (`.gha`) · MIT license

---

## English

### What it does

Which component is drawing this, and where is it in the tree? Click any Grasshopper preview geometry in Rhino and the canvas widget shows its full address: source component, output, data tree path `{a;b}` and item index.

The widget has no interface text, so it reads the same in any language:

- **Pick** (cursor with circles): click a preview in Rhino. A dedicated orange "GH" reticle is shown while picking (object snaps off).
- **Candidate list**: every preview under the cursor, most likely first. Edges and curves come before faces; objects hidden behind the first surface are filtered out unless *Catch behind* is ticked in the widget's right-click menu.
- **Find it on the canvas**: the source component is selected, centred and framed in orange; the geometry is highlighted in Rhino.
- **Copy**: click the address block to copy `{a;b}[i]` (a tick confirms).
- **Isolate** (butterfly net): places a native *Tree Item* set to that path and index, plus a Geometry parameter, in an orange group (Ctrl+Z to undo).
- **Clear** (eraser): removes the highlight, the frame and the list.
- **Widget**: sits in a corner of the canvas (right-click to pick the corner), collapses to its header, and can be hidden or shown again from *Display › Canvas Widgets*. A ⚠ icon appears if something fails; hover it for details.

Supported geometry: points, point clouds, curves, surfaces, Breps, extrusions, meshes and SubD.

### Install

Download `AdresseGH.gha` from the [Releases](https://github.com/GyciH/Address-Picker/releases) page, copy it to `%AppData%\Grasshopper\Libraries`, right-click › Properties › **Unblock**, restart Rhino.

### Known limits (v1)

- Geometry only: planes, vectors, text and dimensions are ignored.
- Clusters are seen from outside (cluster output).
- The *Isolate* address is fixed: if the tree changes upstream, it may point to another item.

---

## Français

### Ce que ça fait

Quel composant dessine cet objet, et où est-il dans l'arbre ? Cliquez une géométrie de preview Grasshopper dans Rhino : le widget du canvas affiche son adresse complète, c'est-à-dire composant source, sortie, chemin `{a;b}` et index.

Le widget n'a aucun texte d'interface : il se lit de la même façon dans toutes les langues.

- **Pointer** (curseur à cercles) : cliquer une preview dans Rhino, avec un réticule orange « GH » pendant le pointage (accrochage coupé).
- **Liste des candidats** : toutes les previews sous le curseur, la plus probable en premier. Les arêtes et courbes passent avant les faces. Ce qui est caché derrière la première surface est écarté, sauf si *Catch behind* est coché dans le menu clic droit du widget.
- **Retrouver sur le canvas** : le composant est sélectionné, centré et encadré en orange, et la géométrie est surlignée dans Rhino.
- **Copier** : un clic sur le bloc adresse copie `{a;b}[i]` (une coche le confirme).
- **Isoler** (filet à papillon) : pose un *Tree Item* natif réglé sur ce chemin et cet index, avec un paramètre Geometry, dans un groupe orange (Ctrl+Z pour annuler).
- **Effacer** (gomme) : retire le surlignage, le cadre et la liste.
- **Widget** : dans un angle du canvas (clic droit pour choisir l'angle), repliable par son en-tête, masquable ou réaffichable via *Display › Canvas Widgets*. Une icône ⚠ apparaît en cas d'erreur ; le détail s'affiche au survol.

### Installation

Télécharger `AdresseGH.gha` depuis [Releases](https://github.com/GyciH/Address-Picker/releases), le copier dans `%AppData%\Grasshopper\Libraries`, clic droit › Propriétés › **Débloquer**, relancer Rhino.

---

## Build

```
cd src
dotnet build -c Release
```

Requires the .NET 8 SDK (or later). The project targets `net7.0` (Rhino 8) and references the `Grasshopper` 8 NuGet package. WinForms and System.Drawing come from the `Microsoft.WindowsDesktop.App.Ref` reference pack, so it also builds on Linux or macOS. Output: `src/bin/Release/net7.0/AdresseGH.gha`.

## Repository layout

| Path | Content |
|---|---|
| `src/` | Plug-in source: widget, picking, highlight, isolate, cursor |
| `docs/food4rhino/` | Food4Rhino listing texts (EN/FR) and icons |
| `docs/maquette-scripteditor.cs` | Original prototype for Rhino 8 ScriptEditor (C#) |

## License

[MIT](LICENSE) © 2026 Jérémy Carolus
