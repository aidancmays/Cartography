using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Cartography
{
    /// <summary>
    /// A small hand-inked mark for a map (a dot, an arrow, a cross...). Size it with the rect transform; colour it with
    /// the graphic's colour; turn it with the rect transform (an arrow points along its up axis).
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("UI/Cartography/Map Mark")]
    public sealed class MapMark : MaskableGraphic
    {
        public const string ShaderResource = "Cartography/InkMark";

        [SerializeField] MarkShape shape = MarkShape.Dot;

        static readonly Dictionary<MarkShape, Material> materials = new();
        static readonly int ShapeId = Shader.PropertyToID("_Shape");

        public MarkShape Shape
        {
            get => shape;
            set
            {
                if (shape == value)
                    return;
                shape = value;
                SetMaterialDirty();
            }
        }

        public override Material material
        {
            get
            {
                if (m_Material != null)
                    return m_Material;
                if (!materials.TryGetValue(shape, out var m) || m == null)
                {
                    var s = Resources.Load<Shader>(ShaderResource);
                    if (s == null)
                        return base.material;
                    m = new Material(s) { name = $"Map Mark {shape}", hideFlags = HideFlags.DontSave };
                    m.SetFloat(ShapeId, (int)shape);
                    materials[shape] = m;
                }
                return m;
            }
            set => base.material = value;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            SetMaterialDirty();
        }
#endif
    }
}
