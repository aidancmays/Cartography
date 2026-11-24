# Changelog

## 0.2.0

- `MapView.LocalToWorld` and `MapView.ShownCentre`, for panning and zooming about the cursor.

## 0.1.0

- `MapBuilder`: regions (box footprints turned about Y) on layers, connections, walls and features, laid out on a
  grid per layer.
- `Map`: which region and layer a point is on, reveal around a point (limited to the region and its neighbours),
  reveal a region or everything, pack / unpack what has been seen (`SaveReveal` / `LoadReveal`).
- `MapView`: draws a layer as an inked map (paper, wash, outlines, walls, hatching, ragged reveal edge), centred on a
  world point at a chosen scale; `WorldToLocal` for placing marks.
- `MapMark`: inked dot, arrow, cross, ring, steps and square marks.
- `Cartography.Masonry` (only compiled with Masonry 0.4.0+): `MasonryMap.Build(LayoutBuilder)`.
