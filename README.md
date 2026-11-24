# Cartography

Maps that reveal as you explore, drawn as inked, hand-printed plans.

## Concepts

- **Region**: one room or area, a box footprint turned about Y, with a height range. Regions are joined to their
  neighbours (doorways, open ground).
- **Layer**: a storey (0 = surface, 1 = one level down...). Each layer is a grid of cells (0.5 m by default) holding
  floor, walls, which region each cell is in, and how much of it has been seen.
- **Feature**: a point of interest (stairs, an exit, an objective). The game decides what each `Kind` means.

## Use

```csharp
var builder = new MapBuilder();
int hall = builder.AddRegion(layer: 0, center, new Vector2(12, 12), yaw: 0, minY: 0, maxY: 4);
int crypt = builder.AddRegion(layer: 1, center2, new Vector2(12, 12), 0, -6, -2);
builder.Connect(hall, crypt);
builder.AddWall(0, from, to);
Map map = builder.Build();

// Every frame, for everyone who should uncover the map:
map.RevealAround(player.position, radius: 9f);

// Which layer to show:
view.Show(map, map.LayerAt(player.position, fallback: view.LayerIndex));
view.Focus = player.position;
```

`MapView` is a UI graphic. Place `MapMark`s as its children with `view.WorldToLocal(worldPosition)`.

Revealing is limited to the region the viewer stands in and its neighbours, so closed walls hide what is behind them.

### Multiplayer

Nothing is networked. Build the same map on every machine (same level = same map), reveal around every player each
machine can see, and send `map.SaveReveal()` to anyone who joins late (`map.LoadReveal(bytes)` merges it).

### Masonry

With `com.aidancmays.masonry` 0.4.0 or later installed, `Cartography.Masonry.MasonryMap.Build(layoutBuilder)` makes the
map of a built layout: one region per piece, one layer per level, neighbours from joined sockets, walls from the
boundaries that drew a fence or wall (with gaps at joined sockets), stairs features at level links.
