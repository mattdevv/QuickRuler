# QuickRuler for Unity
Ever need a fast way to check how large something is in editor, now you can. This custom tool integrates with the existing toolbar so you can quickly select it and click and drag to get a measurement.

The selected points are projected into the scene view at the moment of selection and onto nearby vertices if desired (add shift modifier). Use right click while dragging the ruler to add segments to the ruler if your measurement is not a straight line, each segment will show its length above and the total length is displayed next to the cursor.

# Controls
- `ctrl+Q` to quick select the tool
- `left mouse` and drag to measure between two projected points
- `right mouse` while dragging to add segments to the measurement
- `+shift` while the tool is active to switch to vertex snapping mode

---

## Installation
QuickRuler is a unity package and can be installed it from Package Manager.

Option 1: Requires Git, [install package via Github](https://docs.unity3d.com/Manual/upm-ui-giturl.html) by going to the Package Manger and installing via git URL with the link: `https://github.com/mattdevv/QuickRuler.git`

Option 2: Clone or download this Github project and [install it as a local package](https://docs.unity3d.com/Manual/upm-ui-local.html) or simply drop the extracted folder inside Assets or Packages.

## To Do
- Add different measurement units
