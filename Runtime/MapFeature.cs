using UnityEngine;

namespace Cartography
{
    /// <summary>Shapes a <see cref="MapMark"/> can draw.</summary>
    public enum MarkShape
    {
        Dot,
        /// <summary>Points along the mark's up axis: turn it to show facing.</summary>
        Arrow,
        Cross,
        Ring,
        /// <summary>Three stacked bars: stairs, a way up or down.</summary>
        Steps,
        Square,
    }

    /// <summary>
    /// A point of interest on a map (an exit, stairs, an objective). The game decides what a <see cref="Kind"/> means
    /// and how it is drawn; the map only keeps where it is and on which layer.
    /// </summary>
    public sealed class MapFeature
    {
        public string Kind;
        public Vector3 Position;
        public int Layer;
        /// <summary>Region it stands in, or -1.</summary>
        public int Region = -1;
        public MarkShape Shape = MarkShape.Dot;
        /// <summary>Shown before the spot is seen.</summary>
        public bool AlwaysShown;
        /// <summary>Game data, such as the component the feature came from.</summary>
        public object Tag;

        public MapFeature(string kind, Vector3 position, int layer, MarkShape shape = MarkShape.Dot)
        {
            Kind = kind;
            Position = position;
            Layer = layer;
            Shape = shape;
        }
    }
}
