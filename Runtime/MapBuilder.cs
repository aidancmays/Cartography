using System.Collections.Generic;
using UnityEngine;

namespace Cartography
{
    /// <summary>
    /// Collects regions, walls and features, then lays them out on a grid per layer (<see cref="Build"/>). Feed it
    /// from whatever describes the level: generated pieces (see <c>Cartography.Masonry</c>), hand-placed volumes...
    /// </summary>
    public sealed class MapBuilder
    {
        struct Wall
        {
            public int layer;
            public Vector2 a, b;
        }

        readonly List<MapRegion> regions = new();
        readonly List<Wall> walls = new();
        readonly List<MapFeature> features = new();

        /// <summary>Metres per map cell. Smaller is sharper and costs more memory (each layer is W x H bytes x 4).</summary>
        public float CellSize { get; set; } = 0.5f;
        /// <summary>Empty space kept around each layer's regions, in metres.</summary>
        public float Margin { get; set; } = 6f;
        /// <summary>How thick walls are drawn, in metres.</summary>
        public float WallThickness { get; set; } = 0.6f;
        /// <summary>Largest grid side in cells. Cells grow if a layer would be bigger.</summary>
        public int MaxCells { get; set; } = 1024;

        public int RegionCount => regions.Count;

        /// <summary>Adds a box footprint and returns its region id.</summary>
        /// <param name="center">Centre of the footprint in world space.</param>
        /// <param name="size">Footprint size along the box's own X and Z, in metres.</param>
        /// <param name="yaw">Turn about Y, in degrees.</param>
        /// <param name="minY">Lowest point that still counts as inside (for deciding which layer someone is on).</param>
        /// <param name="maxY">Highest point that still counts as inside.</param>
        public int AddRegion(int layer, Vector3 center, Vector2 size, float yaw, float minY, float maxY, object tag = null)
        {
            var r = new MapRegion
            {
                Id = regions.Count,
                Layer = layer,
                Center = center,
                Size = size,
                Yaw = yaw,
                MinY = Mathf.Min(minY, maxY),
                MaxY = Mathf.Max(minY, maxY),
                Tag = tag,
            };
            regions.Add(r);
            return r.Id;
        }

        /// <summary>Joins two regions, so standing in one reveals the other too.</summary>
        public void Connect(int a, int b)
        {
            if (a == b || a < 0 || b < 0 || a >= regions.Count || b >= regions.Count)
                return;
            if (!regions[a].neighbours.Contains(b))
                regions[a].neighbours.Add(b);
            if (!regions[b].neighbours.Contains(a))
                regions[b].neighbours.Add(a);
        }

        /// <summary>Draws a wall from one world point to another (heights are ignored).</summary>
        public void AddWall(int layer, Vector3 from, Vector3 to)
        {
            walls.Add(new Wall { layer = layer, a = new Vector2(from.x, from.z), b = new Vector2(to.x, to.z) });
        }

        public MapFeature AddFeature(MapFeature feature)
        {
            features.Add(feature);
            return feature;
        }

        public MapRegion Region(int id) => regions[id];

        public Map Build()
        {
            var layerIndices = new SortedSet<int>();
            foreach (var r in regions)
                layerIndices.Add(r.Layer);

            var corners = new Vector3[4];
            var layers = new List<MapLayer>();
            foreach (int index in layerIndices)
            {
                // Bounds of every footprint on this layer.
                var min = new Vector2(float.MaxValue, float.MaxValue);
                var max = new Vector2(float.MinValue, float.MinValue);
                float elevation = 0f;
                int count = 0;
                foreach (var r in regions)
                {
                    if (r.Layer != index)
                        continue;
                    r.GetCorners(corners);
                    foreach (var c in corners)
                    {
                        min = Vector2.Min(min, new Vector2(c.x, c.z));
                        max = Vector2.Max(max, new Vector2(c.x, c.z));
                    }
                    elevation += r.MinY;
                    count++;
                }
                min -= Vector2.one * Margin;
                max += Vector2.one * Margin;
                var size = max - min;
                float cell = Mathf.Max(CellSize, Mathf.Max(size.x, size.y) / Mathf.Max(MaxCells, 16));
                int w = Mathf.Max(1, Mathf.CeilToInt(size.x / cell));
                int h = Mathf.Max(1, Mathf.CeilToInt(size.y / cell));
                var layer = new MapLayer(index, min, w, h, cell) { Elevation = count > 0 ? elevation / count : 0f };
                FillRegions(layer);
                DrawWalls(layer);
                layers.Add(layer);
            }

            foreach (var f in features)
            {
                if (f.Region < 0)
                {
                    var layer = layers.Find(l => l.Index == f.Layer);
                    if (layer != null)
                        f.Region = layer.RegionAt(f.Position);
                }
            }

            return new Map(layers, new List<MapRegion>(regions), new List<MapFeature>(features), CellSize);
        }

        void FillRegions(MapLayer layer)
        {
            var corners = new Vector3[4];
            float cell = layer.CellSize;
            foreach (var r in regions)
            {
                if (r.Layer != layer.Index)
                    continue;
                r.GetCorners(corners);
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var c in corners)
                {
                    minX = Mathf.Min(minX, c.x);
                    maxX = Mathf.Max(maxX, c.x);
                    minZ = Mathf.Min(minZ, c.z);
                    maxZ = Mathf.Max(maxZ, c.z);
                }
                int x0 = Mathf.Max(0, Mathf.FloorToInt((minX - layer.Origin.x) / cell) - 1);
                int x1 = Mathf.Min(layer.Width - 1, Mathf.CeilToInt((maxX - layer.Origin.x) / cell) + 1);
                int y0 = Mathf.Max(0, Mathf.FloorToInt((minZ - layer.Origin.y) / cell) - 1);
                int y1 = Mathf.Min(layer.Height - 1, Mathf.CeilToInt((maxZ - layer.Origin.y) / cell) + 1);
                var half = r.Size * 0.5f;
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * layer.Width + x;
                    var world = new Vector3(layer.Origin.x + (x + 0.5f) * cell, 0f, layer.Origin.y + (y + 0.5f) * cell);
                    var l = r.ToLocal(world);
                    // Coverage of the cell, from how far inside the edge its centre is (soft by one cell).
                    float inside = Mathf.Min(half.x - Mathf.Abs(l.x), half.y - Mathf.Abs(l.y));
                    float cover = Mathf.Clamp01(inside / cell + 0.5f);
                    if (cover <= 0f)
                        continue;
                    byte v = (byte)(cover * 255f);
                    if (v > layer.floor[i])
                        layer.floor[i] = v;
                    if (cover >= 0.5f && layer.region[i] == 0)
                        layer.region[i] = (ushort)(r.Id + 1);
                }
            }
        }

        void DrawWalls(MapLayer layer)
        {
            float cell = layer.CellSize;
            float half = WallThickness * 0.5f;
            foreach (var wall in walls)
            {
                if (wall.layer != layer.Index)
                    continue;
                var a = wall.a - layer.Origin;
                var b = wall.b - layer.Origin;
                float reach = half + cell;
                int x0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.x, b.x) - reach) / cell));
                int x1 = Mathf.Min(layer.Width - 1, Mathf.CeilToInt((Mathf.Max(a.x, b.x) + reach) / cell));
                int y0 = Mathf.Max(0, Mathf.FloorToInt((Mathf.Min(a.y, b.y) - reach) / cell));
                int y1 = Mathf.Min(layer.Height - 1, Mathf.CeilToInt((Mathf.Max(a.y, b.y) + reach) / cell));
                var ab = b - a;
                float lengthSq = Mathf.Max(ab.sqrMagnitude, 1e-6f);
                for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    var p = new Vector2((x + 0.5f) * cell, (y + 0.5f) * cell);
                    float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / lengthSq);
                    float d = Vector2.Distance(p, a + ab * t);
                    float cover = Mathf.Clamp01((half - d) / cell + 0.5f);
                    if (cover <= 0f)
                        continue;
                    int i = y * layer.Width + x;
                    byte v = (byte)(cover * 255f);
                    if (v > layer.walls[i])
                        layer.walls[i] = v;
                }
            }
        }
    }
}
