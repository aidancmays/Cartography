using UnityEngine;
using UnityEngine.UI;

namespace Cartography
{
    /// <summary>
    /// Draws one layer of a <see cref="Map"/> into a UI rect as an inked, printed map: blank paper where nothing has
    /// been seen, a watercolour wash over seen floor, inked outlines and walls, and hatching around the rooms. The
    /// view is centred on <see cref="Focus"/> and shows <see cref="MetresAcross"/> metres from top to bottom.
    /// </summary>
    /// <remarks>
    /// The material is an instance owned by this view and updated every frame, so don't put the view under a
    /// stencil <see cref="Mask"/> (it copies the material once); use <see cref="RectMask2D"/> if it needs clipping.
    /// </remarks>
    [RequireComponent(typeof(CanvasRenderer))]
    [AddComponentMenu("UI/Cartography/Map View")]
    public sealed class MapView : MaskableGraphic
    {
        public const string ShaderResource = "Cartography/InkMap";

        [Tooltip("Shader that draws the map. Empty = Cartography's ink map.")]
        [SerializeField] Shader shader;
        [Tooltip("Metres of the map shown from the top of the rect to the bottom.")]
        [SerializeField, Min(1f)] float metresAcross = 90f;
        [Tooltip("Keep the view over the layer instead of showing empty paper past its edge.")]
        [SerializeField] bool clampToLayer = true;

        [Header("Colours")]
        [SerializeField] Color paperColor = new(0.86f, 0.80f, 0.67f, 1f);
        [SerializeField] Color washColor = new(0.66f, 0.55f, 0.40f, 1f);
        [SerializeField] Color inkColor = new(0.16f, 0.12f, 0.09f, 1f);
        [SerializeField] Color fogColor = new(0.78f, 0.72f, 0.60f, 1f);

        Map map;
        MapLayer layer;
        int layerIndex;
        Vector3 focus;
        Material instance;
        Vector4 view;

        static readonly int RevealTexId = Shader.PropertyToID("_RevealTex");
        static readonly int ViewId = Shader.PropertyToID("_View");
        static readonly int LayerSizeId = Shader.PropertyToID("_LayerSize");
        static readonly int RectSizeId = Shader.PropertyToID("_RectSize");
        static readonly int PaperId = Shader.PropertyToID("_PaperColor");
        static readonly int WashId = Shader.PropertyToID("_WashColor");
        static readonly int InkId = Shader.PropertyToID("_InkColor");
        static readonly int FogId = Shader.PropertyToID("_FogColor");

        public Map Map => map;

        /// <summary>The layer being drawn, or null.</summary>
        public MapLayer CurrentLayer => layer;

        /// <summary>Index of the layer to draw. Changing it switches layers straight away.</summary>
        public int LayerIndex
        {
            get => layerIndex;
            set
            {
                if (layerIndex == value && (layer != null || map == null))
                    return;
                layerIndex = value;
                Refresh();
            }
        }

        /// <summary>World point at the centre of the view.</summary>
        public Vector3 Focus
        {
            get => focus;
            set => focus = value;
        }

        public float MetresAcross
        {
            get => metresAcross;
            set => metresAcross = Mathf.Max(1f, value);
        }

        public bool ClampToLayer
        {
            get => clampToLayer;
            set => clampToLayer = value;
        }

        /// <summary>Shows a map (null clears the view).</summary>
        public void Show(Map value, int layerIndexToShow)
        {
            map = value;
            layerIndex = layerIndexToShow;
            Refresh();
        }

        void Refresh()
        {
            layer = map?.Layer(layerIndex);
            SetMaterialDirty();
            UpdateView();
        }

        public override Texture mainTexture => layer != null ? layer.Plan : Texture2D.blackTexture;

        public override Material material
        {
            get
            {
                if (instance == null)
                {
                    var s = shader != null ? shader : Resources.Load<Shader>(ShaderResource);
                    if (s == null)
                        return base.material;
                    instance = new Material(s) { name = "Map View (instance)", hideFlags = HideFlags.DontSave };
                }
                return instance;
            }
            set => base.material = value;
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            if (instance != null)
            {
                if (Application.isPlaying) Destroy(instance);
                else DestroyImmediate(instance);
            }
        }

        void LateUpdate() => UpdateView();

        void UpdateView()
        {
            var m = material;
            var rect = rectTransform.rect;
            if (m == null || rect.height <= 0f)
                return;

            m.SetVector(RectSizeId, new Vector4(rect.width, rect.height, 0f, 0f));
            m.SetColor(PaperId, paperColor);
            m.SetColor(WashId, washColor);
            m.SetColor(InkId, inkColor);
            m.SetColor(FogId, fogColor);

            if (layer == null)
            {
                m.SetTexture(RevealTexId, Texture2D.blackTexture);
                return;
            }

            layer.UploadReveal();
            m.SetTexture(RevealTexId, layer.RevealTexture);
            var extent = layer.Extent;
            m.SetVector(LayerSizeId, new Vector4(extent.x, extent.y, layer.Width, layer.Height));

            var size = new Vector2(metresAcross * rect.width / rect.height / extent.x, metresAcross / extent.y);
            var centre = layer.WorldToUv(focus);
            if (clampToLayer)
            {
                centre.x = size.x >= 1f ? 0.5f : Mathf.Clamp(centre.x, size.x * 0.5f, 1f - size.x * 0.5f);
                centre.y = size.y >= 1f ? 0.5f : Mathf.Clamp(centre.y, size.y * 0.5f, 1f - size.y * 0.5f);
            }
            view = new Vector4(centre.x - size.x * 0.5f, centre.y - size.y * 0.5f, size.x, size.y);
            m.SetVector(ViewId, view);
        }

        /// <summary>
        /// Where a world point falls in this view's rect, in the rect transform's local space. Use it to place marks
        /// (children of this view) with <c>localPosition</c>.
        /// </summary>
        public Vector2 WorldToLocal(Vector3 world)
        {
            var rect = rectTransform.rect;
            if (layer == null || view.z <= 0f || view.w <= 0f)
                return rect.center;
            var uv = layer.WorldToUv(world);
            var t = new Vector2((uv.x - view.x) / view.z, (uv.y - view.y) / view.w);
            return rect.min + Vector2.Scale(t, rect.size);
        }

        /// <summary>True if a local point (from <see cref="WorldToLocal"/>) is inside the rect, less a margin.</summary>
        public bool InView(Vector2 local, float margin = 0f)
        {
            var rect = rectTransform.rect;
            return local.x >= rect.xMin + margin && local.x <= rect.xMax - margin
                && local.y >= rect.yMin + margin && local.y <= rect.yMax - margin;
        }

        /// <summary>Canvas units per metre at the current zoom.</summary>
        public float UnitsPerMetre => rectTransform.rect.height / metresAcross;
    }
}
