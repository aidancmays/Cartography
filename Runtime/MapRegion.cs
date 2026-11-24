using System.Collections.Generic;
using UnityEngine;

namespace Cartography
{
    /// <summary>
    /// One room or area of a map: a box footprint turned about Y, on one layer. Revealing is limited to the region
    /// the viewer stands in and the regions joined to it, so walls between unconnected rooms keep their secrets.
    /// </summary>
    public sealed class MapRegion
    {
        public int Id { get; internal set; }
        public int Layer { get; internal set; }
        /// <summary>Centre of the footprint in world space (Y is the middle of its height range).</summary>
        public Vector3 Center { get; internal set; }
        /// <summary>Footprint size in metres, along the region's own X and Z.</summary>
        public Vector2 Size { get; internal set; }
        /// <summary>Turn about Y in degrees.</summary>
        public float Yaw { get; internal set; }
        public float MinY { get; internal set; }
        public float MaxY { get; internal set; }

        /// <summary>Regions joined to this one (doorways, open ground).</summary>
        public IReadOnlyList<int> Neighbours => neighbours;
        internal readonly List<int> neighbours = new();

        /// <summary>Game data attached when the region was added, such as the piece it came from.</summary>
        public object Tag { get; internal set; }

        /// <summary>Position in the region's own frame: x/y across the footprint, from its centre.</summary>
        public Vector2 ToLocal(Vector3 world)
        {
            float dx = world.x - Center.x, dz = world.z - Center.z;
            float r = -Yaw * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            // Inverse of a Unity Y turn (clockwise seen from above).
            return new Vector2(dx * c + dz * s, -dx * s + dz * c);
        }

        /// <summary>True when the point lies over the footprint (within <paramref name="margin"/> metres).</summary>
        public bool ContainsFlat(Vector3 world, float margin = 0f)
        {
            var l = ToLocal(world);
            return Mathf.Abs(l.x) <= Size.x * 0.5f + margin && Mathf.Abs(l.y) <= Size.y * 0.5f + margin;
        }

        /// <summary>How far the height is outside the region's height range (0 = inside).</summary>
        public float VerticalDistance(float y) => y < MinY ? MinY - y : y > MaxY ? y - MaxY : 0f;

        /// <summary>World-space corners of the footprint, at the region's centre height.</summary>
        public void GetCorners(Vector3[] into)
        {
            float r = Yaw * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            var right = new Vector3(c, 0f, -s) * (Size.x * 0.5f);
            var forward = new Vector3(s, 0f, c) * (Size.y * 0.5f);
            into[0] = Center - right - forward;
            into[1] = Center + right - forward;
            into[2] = Center + right + forward;
            into[3] = Center - right + forward;
        }
    }
}
