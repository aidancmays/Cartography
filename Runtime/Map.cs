using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cartography
{
    /// <summary>
    /// A map of a place made of regions on one or more layers, which is revealed as people move through it. Build one
    /// with <see cref="MapBuilder"/>; show a layer with <see cref="MapView"/>. Nothing here is networked: every
    /// machine can build the same map from the same level, reveal around everyone it can see, and swap
    /// <see cref="SaveReveal"/> / <see cref="LoadReveal"/> for anyone who joins late.
    /// </summary>
    public sealed class Map : IDisposable
    {
        readonly List<MapLayer> layers;
        readonly List<MapRegion> regions;
        readonly List<MapFeature> features;
        readonly HashSet<int> allowedScratch = new();

        internal Map(List<MapLayer> layers, List<MapRegion> regions, List<MapFeature> features, float cellSize)
        {
            this.layers = layers;
            this.regions = regions;
            this.features = features;
            CellSize = cellSize;
        }

        /// <summary>Layers in order of their index (the surface first).</summary>
        public IReadOnlyList<MapLayer> Layers => layers;
        public IReadOnlyList<MapRegion> Regions => regions;
        public IReadOnlyList<MapFeature> Features => features;
        public float CellSize { get; }

        /// <summary>Raised after a call revealed something new.</summary>
        public event Action<Map> Revealed;

        /// <summary>The layer with this index, or null.</summary>
        public MapLayer Layer(int index)
        {
            foreach (var l in layers)
                if (l.Index == index)
                    return l;
            return null;
        }

        /// <summary>
        /// The region a point stands in: over its footprint and closest to its height range (within
        /// <paramref name="verticalSlack"/> metres). -1 when it is over none.
        /// </summary>
        public int RegionAt(Vector3 world, float verticalSlack = 3f)
        {
            int best = -1;
            float bestDistance = float.MaxValue;
            foreach (var r in regions)
            {
                if (!r.ContainsFlat(world))
                    continue;
                float d = r.VerticalDistance(world.y);
                if (d <= verticalSlack && d < bestDistance)
                {
                    best = r.Id;
                    bestDistance = d;
                }
            }
            return best;
        }

        /// <summary>The layer index a point is on, or <paramref name="fallback"/> when it is over no region.</summary>
        public int LayerAt(Vector3 world, int fallback, float verticalSlack = 3f)
        {
            int r = RegionAt(world, verticalSlack);
            return r >= 0 ? regions[r].Layer : fallback;
        }

        /// <summary>
        /// Reveals around someone standing at a point: cells within the radius, limited to the region they stand in
        /// and (with <paramref name="neighbours"/>) the regions joined to it. Someone over no region reveals nothing.
        /// </summary>
        /// <returns>The region they stand in, or -1.</returns>
        public int RevealAround(Vector3 world, float radius, float soft = 2f, bool neighbours = true)
        {
            int id = RegionAt(world);
            if (id < 0)
                return -1;
            var region = regions[id];
            var layer = Layer(region.Layer);
            allowedScratch.Clear();
            allowedScratch.Add(id);
            if (neighbours)
                foreach (int n in region.neighbours)
                    allowedScratch.Add(n);
            if (layer.Reveal(world, radius, soft, allowedScratch.Contains))
                Revealed?.Invoke(this);
            return id;
        }

        /// <summary>Reveals one whole region at once.</summary>
        public void RevealRegion(int id)
        {
            if (id < 0 || id >= regions.Count)
                return;
            if (Layer(regions[id].Layer).RevealRegion(id))
                Revealed?.Invoke(this);
        }

        /// <summary>Reveals everything (debug, or a found map).</summary>
        public void RevealAll()
        {
            foreach (var l in layers)
            {
                for (int i = 0; i < l.seen.Length; i++)
                    l.seen[i] = 255;
                l.MarkRevealed();
            }
            Revealed?.Invoke(this);
        }

        /// <summary>Forgets everything seen.</summary>
        public void ClearReveal()
        {
            foreach (var l in layers)
                l.ClearReveal();
            Revealed?.Invoke(this);
        }

        /// <summary>How much of the spot has been seen, 0..1.</summary>
        public float SeenAt(Vector3 world, int layer)
        {
            var l = Layer(layer);
            return l != null ? l.SeenAt(world) : 0f;
        }

        /// <summary>
        /// Everything seen so far, as one bit per cell (seen at least halfway), run-length packed. Small enough to send
        /// to a late joiner in one message.
        /// </summary>
        public byte[] SaveReveal()
        {
            var bytes = new List<byte>(256);
            WriteVarint(bytes, (uint)layers.Count);
            foreach (var l in layers)
            {
                WriteVarint(bytes, (uint)l.seen.Length);
                // Alternating runs, starting with unseen.
                bool current = false;
                uint run = 0;
                foreach (byte s in l.seen)
                {
                    bool on = s >= 128;
                    if (on == current)
                    {
                        run++;
                        continue;
                    }
                    WriteVarint(bytes, run);
                    current = on;
                    run = 1;
                }
                WriteVarint(bytes, run);
            }
            return bytes.ToArray();
        }

        /// <summary>
        /// Adds what another machine has seen (from <see cref="SaveReveal"/>) to this map. Nothing already seen is
        /// forgotten. Ignored if the data was made for a different map.
        /// </summary>
        /// <returns>False if the data does not fit this map.</returns>
        public bool LoadReveal(byte[] data)
        {
            if (data == null)
                return false;
            int p = 0;
            if (!ReadVarint(data, ref p, out uint layerCount) || layerCount != layers.Count)
                return false;
            // Check it all fits before changing anything.
            int check = p;
            foreach (var l in layers)
            {
                if (!ReadVarint(data, ref check, out uint length) || length != l.seen.Length)
                    return false;
                uint total = 0;
                while (total < length)
                {
                    if (!ReadVarint(data, ref check, out uint run))
                        return false;
                    total += run;
                }
                if (total != length)
                    return false;
            }

            bool any = false;
            foreach (var l in layers)
            {
                ReadVarint(data, ref p, out uint length);
                int i = 0;
                bool on = false;
                bool changed = false;
                while (i < length)
                {
                    ReadVarint(data, ref p, out uint run);
                    if (on)
                        for (int k = 0; k < run; k++)
                        {
                            if (l.seen[i + k] < 255)
                            {
                                l.seen[i + k] = 255;
                                changed = true;
                            }
                        }
                    i += (int)run;
                    on = !on;
                }
                if (changed)
                {
                    l.MarkRevealed();
                    any = true;
                }
            }
            if (any)
                Revealed?.Invoke(this);
            return true;
        }

        static void WriteVarint(List<byte> into, uint value)
        {
            while (value >= 0x80)
            {
                into.Add((byte)(value | 0x80));
                value >>= 7;
            }
            into.Add((byte)value);
        }

        static bool ReadVarint(byte[] data, ref int p, out uint value)
        {
            value = 0;
            int shift = 0;
            while (p < data.Length && shift < 35)
            {
                byte b = data[p++];
                value |= (uint)(b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                    return true;
                shift += 7;
            }
            return false;
        }

        /// <summary>Frees the layers' textures.</summary>
        public void Dispose()
        {
            foreach (var l in layers)
                l.Dispose();
        }
    }
}
