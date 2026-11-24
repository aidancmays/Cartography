using System;
using System.Collections.Generic;
using Masonry;
using UnityEngine;

namespace Cartography.Masonry
{
    /// <summary>
    /// Makes a <see cref="Map"/> from a built <see cref="LayoutBuilder"/>: one region per piece (its bounds), one
    /// layer per level (<see cref="Piece.Level"/>: the builder's own set is layer 0, the levels it links down to are
    /// 1, 2...), pieces joined by sockets become neighbours, and every boundary that drew a fence or wall becomes a
    /// wall, with a gap where its socket was joined. Stairs between levels (<see cref="LevelLink"/>) become
    /// features of kind <see cref="StairsKind"/> on both layers they join.
    /// </summary>
    public static class MasonryMap
    {
        public const string StairsKind = "Stairs";

        [Serializable]
        public struct Options
        {
            [Tooltip("Metres per map cell.")]
            public float cellSize;
            [Tooltip("Width of the gap left in a wall where a socket was joined, in metres.")]
            public float doorWidth;
            [Tooltip("How thick walls are drawn, in metres.")]
            public float wallThickness;
            [Tooltip("Height above a piece's bounds that still counts as standing in it (jumping, stairs).")]
            public float headroom;

            public static Options Default => new()
            {
                cellSize = 0.5f,
                doorWidth = 3f,
                wallThickness = 0.6f,
                headroom = 2f,
            };
        }

        /// <summary>Builds the map of the builder's last build. Region ids follow <see cref="LayoutBuilder.Pieces"/>.</summary>
        /// <param name="feature">Called for every piece, to add features of the game's own (objectives, exits...).</param>
        public static Map Build(LayoutBuilder builder, Options? options = null, Action<Piece, int, MapBuilder> feature = null)
        {
            var o = options ?? Options.Default;
            var map = new MapBuilder
            {
                CellSize = o.cellSize > 0f ? o.cellSize : 0.5f,
                WallThickness = o.wallThickness > 0f ? o.wallThickness : 0.6f,
            };
            if (builder == null)
                return map.Build();

            var pieces = builder.Pieces;
            var regionOf = new Dictionary<Piece, int>(pieces.Count);
            foreach (var piece in pieces)
            {
                if (piece == null)
                    continue;
                var t = piece.transform;
                var b = piece.Bounds;
                var center = t.TransformPoint(b.center);
                var scale = t.lossyScale;
                var size = new Vector2(b.size.x * Mathf.Abs(scale.x), b.size.z * Mathf.Abs(scale.z));
                float yaw = t.eulerAngles.y;
                float minY = t.TransformPoint(b.center - Vector3.up * b.extents.y).y;
                float maxY = t.TransformPoint(b.center + Vector3.up * b.extents.y).y + o.headroom;
                regionOf[piece] = map.AddRegion(piece.Level, center, size, yaw, minY, maxY, piece);
            }

            foreach (var piece in pieces)
            {
                if (piece == null)
                    continue;
                int id = regionOf[piece];
                foreach (var socket in piece.Sockets)
                {
                    if (socket == null || !socket.IsJoined)
                        continue;
                    var other = socket.JoinedTo.GetComponentInParent<Piece>();
                    if (other != null && regionOf.TryGetValue(other, out int otherId))
                        map.Connect(id, otherId);
                }

                foreach (var boundary in piece.Boundaries)
                    if (boundary != null && boundary.Drawn)
                        AddWall(map, piece.Level, boundary, o.doorWidth);

                foreach (var link in piece.GetComponentsInChildren<LevelLink>(true))
                {
                    var p = link.transform.position;
                    map.AddFeature(new MapFeature(StairsKind, p, piece.Level, MarkShape.Steps) { Region = id, Tag = link });
                    // The arrival piece of the linked level stands on the link's spot, on the next layer down.
                    map.AddFeature(new MapFeature(StairsKind, p, piece.Level + 1, MarkShape.Steps) { Tag = link });
                }

                feature?.Invoke(piece, id, map);
            }

            // Pieces that stand on another level's link (the foot of the stairs) are joined to the piece above.
            foreach (var piece in pieces)
            {
                if (piece == null)
                    continue;
                foreach (var link in piece.GetComponentsInChildren<LevelLink>(true))
                {
                    var p = link.transform.position;
                    foreach (var below in pieces)
                    {
                        if (below == null || below.Level != piece.Level + 1)
                            continue;
                        var r = map.Region(regionOf[below]);
                        if (r.ContainsFlat(p, 0.5f))
                            map.Connect(regionOf[piece], r.Id);
                    }
                }
            }

            return map.Build();
        }

        /// <summary>The region id of a piece in a map built by <see cref="Build"/>, or -1.</summary>
        public static int RegionOf(Map map, Piece piece)
        {
            if (map == null || piece == null)
                return -1;
            foreach (var r in map.Regions)
                if (ReferenceEquals(r.Tag, piece))
                    return r.Id;
            return -1;
        }

        static void AddWall(MapBuilder map, int layer, Boundary boundary, float doorWidth)
        {
            var t = boundary.transform;
            var centre = t.position;
            var right = t.right;
            right.y = 0f;
            right = right.sqrMagnitude > 1e-6f ? right.normalized : Vector3.right;
            float half = boundary.Length * 0.5f;
            var a = centre - right * half;
            var b = centre + right * half;

            var socket = boundary.Socket;
            if (socket == null || !socket.IsJoined || doorWidth <= 0f)
            {
                map.AddWall(layer, a, b);
                return;
            }

            // Leave the doorway open, around where the socket sits along the edge.
            float along = Vector3.Dot(socket.transform.position - a, right);
            float gapFrom = Mathf.Clamp(along - doorWidth * 0.5f, 0f, boundary.Length);
            float gapTo = Mathf.Clamp(along + doorWidth * 0.5f, 0f, boundary.Length);
            if (gapFrom > 0.05f)
                map.AddWall(layer, a, a + right * gapFrom);
            if (gapTo < boundary.Length - 0.05f)
                map.AddWall(layer, a + right * gapTo, b);
        }
    }
}
