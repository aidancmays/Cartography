using System;
using UnityEngine;

namespace Cartography
{
    /// <summary>
    /// One layer of a map (a storey: the surface, the crypts below it...). A grid of cells over the layer's ground
    /// that knows which cells are floor, which hold walls, which region each belongs to and how much of it has been
    /// seen. Drawn by <see cref="MapView"/> from two textures: <see cref="Plan"/> (R floor, G walls) and
    /// <see cref="RevealTexture"/> (R seen).
    /// </summary>
    public sealed class MapLayer : IDisposable
    {
        /// <summary>The layer number given when regions were added (0 = surface, 1 = one storey down, ...).</summary>
        public int Index { get; }
        /// <summary>World X/Z of the grid's lower-left corner.</summary>
        public Vector2 Origin { get; }
        public int Width { get; }
        public int Height { get; }
        /// <summary>Metres per cell.</summary>
        public float CellSize { get; }
        /// <summary>Average height of the layer's regions, in world space.</summary>
        public float Elevation { get; internal set; }

        /// <summary>World size of the grid in metres (X, Z).</summary>
        public Vector2 Extent => new(Width * CellSize, Height * CellSize);

        internal readonly byte[] floor;
        internal readonly byte[] walls;
        internal readonly byte[] seen;
        /// <summary>Region id + 1 per cell, 0 = outside every region.</summary>
        internal readonly ushort[] region;

        Texture2D plan;
        Texture2D reveal;
        bool revealDirty = true;

        /// <summary>Bumped every time something new is revealed on this layer.</summary>
        public int RevealVersion { get; private set; }

        internal MapLayer(int index, Vector2 origin, int width, int height, float cellSize)
        {
            Index = index;
            Origin = origin;
            Width = width;
            Height = height;
            CellSize = cellSize;
            int n = width * height;
            floor = new byte[n];
            walls = new byte[n];
            seen = new byte[n];
            region = new ushort[n];
        }

        /// <summary>Floor (R) and walls (G), made on first use.</summary>
        public Texture2D Plan
        {
            get
            {
                if (plan == null)
                {
                    plan = new Texture2D(Width, Height, TextureFormat.RGBA32, false, true)
                    {
                        name = $"Map Layer {Index} Plan",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        hideFlags = HideFlags.DontSave,
                    };
                    var pixels = new Color32[Width * Height];
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i] = new Color32(floor[i], walls[i], 0, 255);
                    plan.SetPixels32(pixels);
                    plan.Apply(false, true);
                }
                return plan;
            }
        }

        /// <summary>How much of each cell has been seen (R), kept up to date by <see cref="UploadReveal"/>.</summary>
        public Texture2D RevealTexture
        {
            get
            {
                if (reveal == null)
                {
                    reveal = new Texture2D(Width, Height, TextureFormat.R8, false, true)
                    {
                        name = $"Map Layer {Index} Reveal",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Bilinear,
                        hideFlags = HideFlags.DontSave,
                    };
                    revealDirty = true;
                }
                if (revealDirty)
                    UploadReveal();
                return reveal;
            }
        }

        /// <summary>Sends newly revealed cells to the GPU. Cheap when nothing changed.</summary>
        public void UploadReveal()
        {
            if (reveal == null || !revealDirty)
                return;
            reveal.SetPixelData(seen, 0);
            reveal.Apply(false, false);
            revealDirty = false;
        }

        /// <summary>World X/Z to grid UV (0..1 across the layer).</summary>
        public Vector2 WorldToUv(Vector3 world) =>
            new((world.x - Origin.x) / (Width * CellSize), (world.z - Origin.y) / (Height * CellSize));

        /// <summary>Grid UV to world X/Z, at the layer's elevation.</summary>
        public Vector3 UvToWorld(Vector2 uv) =>
            new(Origin.x + uv.x * Width * CellSize, Elevation, Origin.y + uv.y * Height * CellSize);

        public bool TryCell(Vector3 world, out int x, out int y)
        {
            x = Mathf.FloorToInt((world.x - Origin.x) / CellSize);
            y = Mathf.FloorToInt((world.z - Origin.y) / CellSize);
            return x >= 0 && y >= 0 && x < Width && y < Height;
        }

        /// <summary>How much of the cell under this point has been seen, 0..1.</summary>
        public float SeenAt(Vector3 world) => TryCell(world, out int x, out int y) ? seen[y * Width + x] / 255f : 0f;

        /// <summary>True if the point is over floor.</summary>
        public bool IsFloor(Vector3 world) => TryCell(world, out int x, out int y) && floor[y * Width + x] >= 128;

        /// <summary>Region id under this point, or -1.</summary>
        public int RegionAt(Vector3 world) => TryCell(world, out int x, out int y) ? region[y * Width + x] - 1 : -1;

        /// <summary>Fraction of this layer's floor cells seen at least halfway.</summary>
        public float SeenFraction()
        {
            int floorCells = 0, seenCells = 0;
            for (int i = 0; i < floor.Length; i++)
            {
                if (floor[i] < 128)
                    continue;
                floorCells++;
                if (seen[i] >= 128)
                    seenCells++;
            }
            return floorCells == 0 ? 0f : (float)seenCells / floorCells;
        }

        /// <summary>
        /// Reveals cells within <paramref name="radius"/> of a point, fading out over the last <paramref name="soft"/>
        /// metres. <paramref name="allowed"/> limits which regions' cells may be revealed (cells outside any region
        /// are always allowed, so walls and their surroundings show).
        /// </summary>
        /// <returns>True if anything new was revealed.</returns>
        public bool Reveal(Vector3 world, float radius, float soft, Func<int, bool> allowed = null)
        {
            float cx = (world.x - Origin.x) / CellSize - 0.5f;
            float cy = (world.z - Origin.y) / CellSize - 0.5f;
            float r = radius / CellSize;
            float s = Mathf.Max(soft / CellSize, 0.001f);
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - r)), x1 = Mathf.Min(Width - 1, Mathf.CeilToInt(cx + r));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - r)), y1 = Mathf.Min(Height - 1, Mathf.CeilToInt(cy + r));
            bool changed = false;
            for (int y = y0; y <= y1; y++)
            {
                float dy = y - cy;
                for (int x = x0; x <= x1; x++)
                {
                    float dx = x - cx;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > r)
                        continue;
                    int i = y * Width + x;
                    if (allowed != null && region[i] != 0 && !allowed(region[i] - 1))
                        continue;
                    byte v = (byte)(Mathf.Clamp01((r - d) / s) * 255f);
                    if (v > seen[i])
                    {
                        seen[i] = v;
                        changed = true;
                    }
                }
            }
            if (changed)
                MarkRevealed();
            return changed;
        }

        /// <summary>Reveals every cell of one region, plus a rim of cells around it so its walls show.</summary>
        public bool RevealRegion(int regionId, int rimCells = 3)
        {
            ushort id = (ushort)(regionId + 1);
            bool changed = false;
            for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                if (region[y * Width + x] != id)
                    continue;
                for (int oy = -rimCells; oy <= rimCells; oy++)
                for (int ox = -rimCells; ox <= rimCells; ox++)
                {
                    int nx = x + ox, ny = y + oy;
                    if (nx < 0 || ny < 0 || nx >= Width || ny >= Height)
                        continue;
                    int i = ny * Width + nx;
                    ushort other = region[i];
                    if (other != 0 && other != id)
                        continue;
                    if (seen[i] != 255)
                    {
                        seen[i] = 255;
                        changed = true;
                    }
                }
            }
            if (changed)
                MarkRevealed();
            return changed;
        }

        /// <summary>Forgets everything seen on this layer.</summary>
        public void ClearReveal()
        {
            Array.Clear(seen, 0, seen.Length);
            MarkRevealed();
        }

        internal void MarkRevealed()
        {
            revealDirty = true;
            RevealVersion++;
        }

        public void Dispose()
        {
            DestroyTexture(plan);
            DestroyTexture(reveal);
            plan = null;
            reveal = null;
        }

        static void DestroyTexture(Texture2D texture)
        {
            if (texture == null)
                return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
