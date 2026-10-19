using Defend.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Defend.Unity
{
    /// <summary>
    /// 展示预制体中已保存的纯视觉模型。模型不含战斗脚本，预览只改变相机视角。
    /// 此组件应挂在 RawImage 上；相机和模型另置于独立预览层及远离战场的空间。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public sealed class ArchiveModelPreview : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        [SerializeField] private Camera previewCamera;
        [SerializeField] private Transform modelRoot;
        [SerializeField] private GameObject[] buildingModels = new GameObject[8];
        [SerializeField] private GameObject[] enemyModels = new GameObject[5];
        [SerializeField, Range(10f, 50f)] private float elevation = 22f;
        [SerializeField] private float initialAzimuth = -35f;

        private RawImage output;
        private RectTransform outputRect;
        private Canvas ownerCanvas;
        private RenderTexture renderTexture;
        private Transform previewStage;
        private bool ownsDetachedStage;
        private GameObject currentModel;
        private Vector3 localFocus;
        private Vector3 localExtents = Vector3.one;
        private float localRadius = 1f;
        private float azimuth;
        private float zoom = 1f;
        private bool requestedVisible;
        private bool dragging;
        private bool initialized;

        public bool HasModel => currentModel != null;

        public void Configure(Camera camera, Transform models, GameObject[] buildings, GameObject[] enemies)
        {
            Clear();
            if (ownsDetachedStage && (models == null || models.parent != previewStage)) DestroyStage();
            previewCamera = camera;
            modelRoot = models;
            buildingModels = buildings ?? new GameObject[0];
            enemyModels = enemies ?? new GameObject[0];
            initialized = false;
            Initialize();
        }

        private void Awake() { Initialize(); }

        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            output = GetComponent<RawImage>();
            outputRect = (RectTransform)transform;
            ownerCanvas = GetComponentInParent<Canvas>();
            output.texture = null;
            output.enabled = false;
            InitializeStage();
            PrepareModels(buildingModels);
            PrepareModels(enemyModels);
            if (modelRoot != null) modelRoot.gameObject.SetActive(false);
            if (previewCamera != null)
            {
                previewCamera.enabled = false;
                previewCamera.cullingMask = 1 << 31;
                previewCamera.targetTexture = null;
                previewCamera.clearFlags = CameraClearFlags.SolidColor;
                previewCamera.backgroundColor = new Color(0, 0, 0, 0);
                previewCamera.orthographic = false;
                previewCamera.fieldOfView = 35f;
                previewCamera.useOcclusionCulling = false;
                previewCamera.allowHDR = false;
                previewCamera.allowMSAA = false;
            }
        }

        private void InitializeStage()
        {
            Transform candidate = modelRoot != null ? modelRoot.parent : null;
            // 制作器保存的舞台只包含模型、相机与专用灯，不能脱离任何 UI 祖先。
            if (candidate == null || previewCamera == null || transform.IsChildOf(candidate) ||
                !previewCamera.transform.IsChildOf(candidate)) return;
            previewStage = candidate;
            previewStage.gameObject.SetActive(false);
            if (!Application.isPlaying) return;
            previewStage.SetParent(null, false);
            previewStage.SetPositionAndRotation(new Vector3(10000f, 10000f, 10000f), Quaternion.identity);
            previewStage.localScale = Vector3.one;
            ownsDetachedStage = true;
        }

        public bool Show(BuildingKind kind) { return Select(buildingModels, (int)kind); }
        public bool Show(EnemyKind kind) { return Select(enemyModels, (int)kind); }

        private bool Select(GameObject[] models, int index)
        {
            Initialize();
            if (currentModel != null) currentModel.SetActive(false);
            currentModel = models != null && index >= 0 && index < models.Length ? models[index] : null;
            if (currentModel != null && (modelRoot == null || !currentModel.transform.IsChildOf(modelRoot)))
                currentModel = null;
            if (currentModel != null)
            {
                currentModel.SetActive(true);
                if (!TryGetVisualBounds(currentModel, out Bounds bounds))
                {
                    currentModel.SetActive(false);
                    currentModel = null;
                }
                else
                {
                    localFocus = modelRoot.InverseTransformPoint(bounds.center);
                    localExtents = bounds.extents / RootScale();
                    localRadius = Mathf.Max(.01f, bounds.extents.magnitude) / RootScale();
                }
            }
            ResetView();
            ApplyVisibility();
            return HasModel;
        }

        public void SetVisible(bool visible)
        {
            Initialize();
            requestedVisible = visible;
            if (!visible) dragging = false;
            ApplyVisibility();
        }

        public void Clear()
        {
            requestedVisible = false;
            dragging = false;
            if (currentModel != null) currentModel.SetActive(false);
            currentModel = null;
            if (modelRoot != null) modelRoot.gameObject.SetActive(false);
            if (previewStage != null) previewStage.gameObject.SetActive(false);
            ReleaseTexture();
        }

        public void ResetView()
        {
            azimuth = initialAzimuth;
            zoom = 1f;
            UpdateCameraPose();
        }

        private void ApplyVisibility()
        {
            bool visible = requestedVisible && isActiveAndEnabled && HasModel && previewCamera != null && modelRoot != null;
            if (previewStage != null) previewStage.gameObject.SetActive(visible);
            if (modelRoot != null) modelRoot.gameObject.SetActive(visible);
            if (!visible) { ReleaseTexture(); return; }
            EnsureTexture();
            UpdateCameraPose();
            output.enabled = true;
            previewCamera.enabled = true;
        }

        private void LateUpdate()
        {
            if (!requestedVisible || !HasModel || previewCamera == null || !previewCamera.enabled) return;
            // Canvas 缩放变化后只重建尺寸已变化的纹理；不在此处遍历模型或创建材质。
            EnsureTexture();
            UpdateCameraPose();
        }

        private void EnsureTexture()
        {
            float scale = ownerCanvas != null ? ownerCanvas.scaleFactor : 1f;
            Vector2 size = outputRect.rect.size * scale;
            float factor = Mathf.Min(1f, 1536f / Mathf.Max(1f, Mathf.Max(size.x, size.y)));
            int width = Mathf.Max(32, Mathf.CeilToInt(size.x * factor));
            int height = Mathf.Max(32, Mathf.CeilToInt(size.y * factor));
            if (renderTexture != null && renderTexture.width == width && renderTexture.height == height) return;
            ReleaseTexture();
            renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            renderTexture.name = "Archive model preview";
            renderTexture.filterMode = FilterMode.Bilinear;
            previewCamera.targetTexture = renderTexture;
            previewCamera.aspect = (float)width / height;
            output.texture = renderTexture;
            output.enabled = true;
            previewCamera.enabled = true;
        }

        private void ReleaseTexture()
        {
            if (previewCamera != null)
            {
                previewCamera.enabled = false;
                previewCamera.targetTexture = null;
            }
            if (output != null) { output.texture = null; output.enabled = false; }
            if (renderTexture == null) return;
            RenderTexture.ReleaseTemporary(renderTexture);
            renderTexture = null;
        }

        private void UpdateCameraPose()
        {
            if (previewCamera == null || modelRoot == null || !HasModel) return;
            Vector3 focus = modelRoot.TransformPoint(localFocus);
            float radius = localRadius * RootScale();
            float verticalHalfAngle = previewCamera.fieldOfView * .5f * Mathf.Deg2Rad;
            float horizontalHalfAngle = Mathf.Atan(Mathf.Tan(verticalHalfAngle) * Mathf.Max(.1f, previewCamera.aspect));
            Quaternion rotation = Quaternion.Euler(elevation, azimuth, 0);
            Quaternion inverseRotation = Quaternion.Inverse(rotation);
            Vector3 extents = localExtents * RootScale();
            float distance = .01f;
            // 依当前视角容纳包围盒，不以外接球取景，避免细长敌人和扁平建筑缩成小点。
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                Vector3 point = inverseRotation * Vector3.Scale(extents, sign);
                distance = Mathf.Max(distance, Mathf.Max(Mathf.Abs(point.x) / Mathf.Tan(horizontalHalfAngle) - point.z,
                    Mathf.Abs(point.y) / Mathf.Tan(verticalHalfAngle) - point.z));
            }
            distance *= 1.08f * zoom;
            previewCamera.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
            previewCamera.nearClipPlane = Mathf.Max(.005f, distance - radius * 1.25f);
            previewCamera.farClipPlane = distance + radius * 3f;
        }

        private float RootScale()
        {
            Vector3 scale = modelRoot.lossyScale;
            return Mathf.Max(.0001f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = eventData.button == PointerEventData.InputButton.Left && CanInteract(eventData);
            if (dragging) eventData.Use();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || !CanInteract(eventData)) return;
            float scale = ownerCanvas != null ? ownerCanvas.scaleFactor : 1f;
            azimuth -= eventData.delta.x * 180f / Mathf.Max(1f, outputRect.rect.width * scale);
            UpdateCameraPose();
            eventData.Use();
        }

        public void OnEndDrag(PointerEventData eventData) { dragging = false; }

        public void OnScroll(PointerEventData eventData)
        {
            if (!CanInteract(eventData)) return;
            zoom = Mathf.Clamp(zoom * Mathf.Exp(-eventData.scrollDelta.y * .08f), .72f, 2.2f);
            UpdateCameraPose();
            eventData.Use();
        }

        private bool CanInteract(PointerEventData eventData)
        {
            return requestedVisible && isActiveAndEnabled && HasModel && renderTexture != null &&
                RectTransformUtility.RectangleContainsScreenPoint(outputRect, eventData.position, eventData.pressEventCamera);
        }

        private void OnDisable() { SetVisible(false); }
        private void OnDestroy()
        {
            ReleaseTexture();
            DestroyStage();
        }

        private void DestroyStage()
        {
            if (ownsDetachedStage && previewStage != null)
            {
                previewStage.gameObject.SetActive(false);
                Destroy(previewStage.gameObject);
            }
            previewStage = null;
            ownsDetachedStage = false;
        }

        private static void PrepareModels(GameObject[] models)
        {
            if (models == null) return;
            foreach (GameObject model in models)
            {
                if (model == null) continue;
                // 新美术副本可能沿用战场层。预览只使用独立层，避免模型存在但相机完全看不到。
                foreach (Transform part in model.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = 31;
                model.SetActive(false);
            }
        }

        private static bool TryGetVisualBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            bool found = false;
            foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                if (!renderer.enabled || !LocallyActive(renderer.transform, model.transform) || !(renderer is MeshRenderer || renderer is SkinnedMeshRenderer)) continue;
                Bounds localBounds;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    // 蒙皮剔除范围包含攻击和倒地的极限姿态，构图应采用当前待机的实际轮廓。
                    var posedMesh = new Mesh();
                    // true 保留此蒙皮模型的网格局部空间；100倍单位换算的FBX在false时会把该比例先烘入顶点。
                    try { skinned.BakeMesh(posedMesh, true); posedMesh.RecalculateBounds(); localBounds = posedMesh.bounds; }
                    finally { if (Application.isPlaying) Destroy(posedMesh); else DestroyImmediate(posedMesh); }
                }
                else
                {
                    MeshFilter filter = renderer.GetComponent<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    localBounds = filter.sharedMesh.bounds;
                }
                // 使用网格局部包围盒，保证尚未渲染、隐藏父节点下的模型也可正确构图。
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 sign = new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1);
                    Vector3 point = renderer.transform.TransformPoint(localBounds.center + Vector3.Scale(localBounds.extents, sign));
                    if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                    else bounds.Encapsulate(point);
                }
            }
            return found;
        }

        private static bool LocallyActive(Transform part, Transform root)
        {
            // 舞台为隐藏状态时 activeInHierarchy 为假，构图仍需排除模型内部关闭的枪焰等节点。
            for (Transform current = part; current != null && current != root; current = current.parent)
                if (!current.gameObject.activeSelf) return false;
            return true;
        }
    }
}
