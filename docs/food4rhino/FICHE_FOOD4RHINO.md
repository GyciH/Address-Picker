# Fiche Food4Rhino : Adresse GH 1.2.0

Textes prêts à coller dans le formulaire « Add new app » de food4rhino.com.
Food4Rhino est en anglais : la version anglaise sert pour la fiche, la version française peut s'ajouter sous l'anglais.

---

## Champs de base

| Champ | Valeur proposée |
|---|---|
| GUID | `6f0c7f3e-3b1e-4a3a-9b8e-2f6c1d9a4e21` |
| Titre | **Adresse GH** (variante plus parlante à l'international : *GH Address Picker*) |
| Type | Grasshopper plug-in |
| Catégorie | Utilities / Data management |
| Rhino | Rhino 8 |
| Système | Windows (macOS non pris en charge : le widget utilise l'interface Windows de Grasshopper) |
| Licence | Open source, MIT |
| Prix | Gratuit |
| Logo | `icon_512.png` |
| Mots-clés | grasshopper, data tree, path, index, preview, picking, debug, widget, tree item, workflow |
| Site / support | https://github.com/GyciH/Address-Picker (onglet Issues pour le support) |

---

## Description courte (EN)

> Click a Grasshopper preview in a Rhino viewport to find its source component, data tree path and index, then isolate it in one click.

## Description courte (FR)

> Cliquez une preview Grasshopper dans Rhino pour retrouver son composant, son chemin et son index, puis isolez-la en un clic.

---

## Description longue (EN)

**Which component is drawing this, and where is it in the tree?**

Adresse GH answers that question directly from the Rhino viewport. Click any Grasshopper preview geometry and you get its full "address": the source component, the output, the data tree path `{a;b}` and the item index.

**Features**

- **Pick from Rhino**: a dedicated orange "GH" reticle replaces the cursor while picking, with object snaps turned off.
- **Smart candidate list**: every preview under the cursor is listed, the most likely one first. Edges and curves come before faces, and objects hidden behind the first surface are filtered out (tick *Catch behind* in the right-click menu to include them).
- **Find it on the canvas**: the source component is selected, centred and framed in orange. The geometry is highlighted in Rhino.
- **Copy the address**: click the address block to copy `{a;b}[i]` to the clipboard.
- **Isolate**: one click places a native *Tree Item* already set to that path and index, plus a Geometry parameter, in an orange group. Ctrl+Z undoes it.
- **Adjustable highlight**: colour, line width and translucent fill, from the widget's right-click menu.
- **Language-free interface**: icon buttons only (pick, butterfly net to isolate, eraser to clear), no interface text.
- **Canvas widget**: lives in a corner of the Grasshopper canvas (choose the corner with a right-click), collapses to a small header, and can be hidden from *Display › Canvas Widgets*.

**Supported geometry**: points, point clouds, curves, surfaces, Breps, extrusions, meshes and SubD.

**Requirements**: Rhino 8 for Windows.

**Source code** (MIT): https://github.com/GyciH/Address-Picker

**Known limits (v1)**

- Geometry only: planes, vectors, text and dimensions are ignored.
- Clusters are seen from outside (the cluster output).
- The address used by *Isolate* is fixed: if the tree changes upstream, it may point to another item.

**Installation**

Download the zip, copy `AdresseGH.gha` to `%AppData%\Grasshopper\Libraries`, right-click › Properties › **Unblock**, restart Rhino.

---

## Description longue (FR)

**Quel composant dessine cet objet, et où est-il dans l'arbre ?**

Adresse GH répond à cette question directement depuis le viewport Rhino. Cliquez une géométrie de preview Grasshopper et vous obtenez son « adresse » complète : le composant source, la sortie, le chemin de l'arbre `{a;b}` et l'index de l'élément.

**Fonctions**

- **Pointage depuis Rhino** : un réticule orange « GH » remplace le curseur pendant le pointage, sans accrochage aux objets.
- **Liste de candidats triée** : toutes les previews sous le curseur sont listées, la plus probable en premier. Les arêtes et courbes passent avant les faces, et ce qui est caché derrière la première surface est écarté (option pour l'inclure).
- **Retrouver sur le canvas** : le composant source est sélectionné, centré et encadré en orange. La géométrie est surlignée dans Rhino.
- **Copier l'adresse** : un clic sur le bloc adresse copie `{a;b}[i]`.
- **Isoler** : un clic pose un *Tree Item* natif déjà réglé sur ce chemin et cet index, avec un paramètre Geometry, dans un groupe orange. Ctrl+Z annule.
- **Surlignage réglable** : couleur, épaisseur et remplissage translucide, depuis le menu clic droit du widget.
- **Interface sans texte** : boutons à icônes uniquement (pointer, filet à papillon pour isoler, gomme pour effacer).
- **Widget de canvas** : dans un angle du canvas (au choix par clic droit), repliable, masquable via *Display › Canvas Widgets*.

**Géométries prises en charge** : points, nuages de points, courbes, surfaces, Breps, extrusions, maillages et SubD.

**Prérequis** : Rhino 8 pour Windows.

---

## Historique des versions

- **1.2.0** (2026-11-19) : surlignage plus visible et réglable (couleur, épaisseur, remplissage).
- **1.1.0** (2026-11-19) : boutons à icônes, plus aucun texte d'interface ; option *Catch behind* dans le menu clic droit.
- **1.0.1** (2026-11-19) : bouton *Clear / Effacer* pour retirer la sélection.
- **1.0.0** (2026-11-18) : première version.

---

## Fichiers à téléverser

| Fichier | Usage |
|---|---|
| `AdresseGH-1.2.0.zip` | Installation manuelle (`.gha` + README anglais) |
| `icon_512.png` | Logo de la fiche (aussi en 256 et 64 px) |

---

## Captures d'écran

Faites le 2026-11-19 dans `docs/screenshots/` (01-pick-overview, 02-isolate, 03-widget-closeup), avec la 1.1.0 : à refaire si on veut montrer le nouveau surlignage.

## Captures d'écran prévues (2 ou 3)

1. **Vue d'ensemble** : Rhino et Grasshopper côte à côte, une arête surlignée en orange, le widget avec la liste et l'adresse, le composant encadré sur le canvas.
2. **Isoler** : le groupe orange avec le Tree Item et le paramètre « Isolé {a;b}[i] », et l'élément seul en preview.
3. **Curseur** (facultatif) : gros plan sur le réticule « GH » au-dessus d'une géométrie.

Une courte vidéo (30 s, pointage puis Isoler) aide beaucoup sur Food4Rhino.
