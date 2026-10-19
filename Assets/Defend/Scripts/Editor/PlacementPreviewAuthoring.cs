using System;
using System.Collections.Generic;
using System.Linq;
using Defend.Simulation;
using Defend.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Defend.Editor
{
    /// <summary>从正式模型提取纯渲染预制体；只更新预览资源和场景绑定。</summary>
    public static class PlacementPreviewAuthoring
    {
        public const string Root = "Assets/Defend/Content/Art/Placement";
        private static readonly Dictionary<Material, Material> ghostMaterials = new Dictionary<Material, Material>();

        [MenuItem("涌现防线/接入建筑摆放预览")]
        public static void Integrate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先结束播放。");
            var scene = SceneManager.GetActiveScene();
            if (scene.isDirty) throw new InvalidOperationException("当前场景有未保存修改，请先保存。");
            if (scene.path != PhaseFiveCombatSceneBuilder.ScenePath) scene = EditorSceneManager.OpenScene(PhaseFiveCombatSceneBuilder.ScenePath);
            IntegrateScene(scene);
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("建筑摆放预览未能保存到场景。");
        }

        public static void IntegrateScene(Scene scene)
        {
            WhiteboxSceneBuilder.EnsureFolder(Root + "/Materials");
            WhiteboxSceneBuilder.EnsureFolder(Root + "/Ghosts");
            ghostMaterials.Clear();
            var session = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<GameSessionRoot>(true)).Single();
            var sessionData = new SerializedObject(session);
            var world = (WorldPresentation)sessionData.FindProperty("presentation").objectReferenceValue;
            var worldData = new SerializedObject(world);
            var sources = worldData.FindProperty("buildingPrefabs");
            var previews = new List<PlacementGhostView>();
            for (int i = 0; i < sources.arraySize; i++)
            {
                var source = (BuildingView)sources.GetArrayElementAtIndex(i).objectReferenceValue;
                if (source.Definition.Kind != BuildingKind.Core) previews.Add(CreateGhost(source));
            }
            Material outline = GroundMaterial("RangeOutline", 0, Color.white);
            LineRenderer rangePrefab = CreateRangePrefab(outline);
            var presenter = session.GetComponentInChildren<PlacementPresentation>(true);
            if (presenter == null)
            {
                var root = new GameObject("建筑摆放预览"); root.transform.SetParent(session.transform, false);
                presenter = root.AddComponent<PlacementPresentation>();
            }
            presenter.Configure(previews.ToArray(), rangePrefab);
            IntegrateLegend(scene);
            EditorUtility.SetDirty(presenter);
            sessionData.FindProperty("placementPresentation").objectReferenceValue = presenter;
            sessionData.FindProperty("energyLossCellPrefab").objectReferenceValue = CreateEfficiencyCell();
            sessionData.ApplyModifiedPropertiesWithoutUndo();
            foreach (string field in new[] { "selectionRange", "previewRange" })
            {
                var line = (LineRenderer)worldData.FindProperty(field).objectReferenceValue;
                if (line == null) continue;
                ConfigureRange(line, outline);
                line.startColor = line.endColor = new Color(.3f, .95f, 1, .75f);
                line.startWidth = line.endWidth = .06f;
                EditorUtility.SetDirty(line);
                PrefabUtility.RecordPrefabInstancePropertyModifications(line);
            }
            var footprint = (Transform)worldData.FindProperty("placementPreview").objectReferenceValue;
            footprint.GetComponent<MeshFilter>().sharedMesh = FootprintMesh();
            var surface = (Renderer)worldData.FindProperty("previewRenderer").objectReferenceValue;
            surface.sharedMaterial = GroundMaterial("Footprint", 2, Color.white);
            surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
            // 预览不参与拾取，真正的建筑仍沿用自身碰撞体。
            foreach (Collider collider in footprint.GetComponents<Collider>()) Object.DestroyImmediate(collider);
            EditorUtility.SetDirty(surface); EditorUtility.SetDirty(footprint.GetComponent<MeshFilter>());
            PrefabUtility.RecordPrefabInstancePropertyModifications(surface);
            PrefabUtility.RecordPrefabInstancePropertyModifications(footprint.GetComponent<MeshFilter>());
        }

        public static PlacementGhostView UpdateBuildingGhost(BuildingView source)
        {
            WhiteboxSceneBuilder.EnsureFolder(Root + "/Materials");
            WhiteboxSceneBuilder.EnsureFolder(Root + "/Ghosts");
            ghostMaterials.Clear();
            return CreateGhost(source);
        }

        private static PlacementGhostView CreateGhost(BuildingView source)
        {
            var root = new GameObject(source.Definition.Kind + "Ghost");
            try
            {
                var copiedTransforms = new Dictionary<Transform, Transform>();
                CopyVisual(source.VisualRoot, root.transform, copiedTransforms);
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) throw new InvalidOperationException("建筑没有可预览模型：" + source.name);
                float bottom = renderers.Min(x => x.bounds.min.y);
                root.transform.GetChild(0).localPosition -= Vector3.up * bottom;
                Transform facingPivot = null;
                if (source.Definition.Kind == BuildingKind.PiercingRailgun)
                {
                    var anchors = source.ModelAnchors;
                    if (anchors == null || anchors.TurretYaw == null || !copiedTransforms.TryGetValue(anchors.TurretYaw, out facingPivot))
                        throw new InvalidOperationException("贯穿轨道炮预览缺少已绑定的水平转向枢轴。");
                }
                var ghost = root.AddComponent<PlacementGhostView>(); ghost.Configure(source.Definition.Kind, renderers, facingPivot);
                return PrefabUtility.SaveAsPrefabAsset(root, Root + "/Ghosts/" + source.Definition.Kind + ".prefab").GetComponent<PlacementGhostView>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static void CopyVisual(Transform source, Transform parent, Dictionary<Transform, Transform> copiedTransforms)
        {
            if (source.GetComponent<SkinnedMeshRenderer>() != null) throw new InvalidOperationException("预览提取尚未支持蒙皮建筑：" + source.name);
            var copy = new GameObject(source.name);
            copy.transform.SetParent(parent, false);
            copiedTransforms.Add(source, copy.transform);
            copy.transform.localPosition = source.localPosition; copy.transform.localRotation = source.localRotation; copy.transform.localScale = source.localScale;
            var mesh = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (mesh != null && renderer != null)
            {
                copy.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
                var surface = copy.AddComponent<MeshRenderer>();
                surface.sharedMaterials = renderer.sharedMaterials.Select(GhostMaterial).ToArray();
                surface.shadowCastingMode = ShadowCastingMode.Off; surface.receiveShadows = false;
                surface.lightProbeUsage = LightProbeUsage.Off; surface.reflectionProbeUsage = ReflectionProbeUsage.Off;
                surface.enabled = renderer.enabled;
            }
            foreach (Transform child in source) CopyVisual(child, copy.transform, copiedTransforms);
            copy.SetActive(source.gameObject.activeSelf);
        }

        private static Material GhostMaterial(Material source)
        {
            if (source == null) throw new InvalidOperationException("建筑模型材质缺失。");
            if (ghostMaterials.TryGetValue(source, out Material result)) return result;
            result = MaterialAt("Ghost_" + ghostMaterials.Count + "_" + source.name, "Defend/Placement/Ghost");
            string color = source.HasProperty("_BaseColor") ? "_BaseColor" : source.HasProperty("_Color") ? "_Color" : null;
            result.SetColor("_BaseColor", color == null ? Color.white : source.GetColor(color));
            string texture = source.HasProperty("_BaseMap") ? "_BaseMap" : source.HasProperty("_MainTex") ? "_MainTex" : null;
            result.SetTexture("_BaseMap", texture == null ? null : source.GetTexture(texture));
            if (texture != null) { result.SetTextureScale("_BaseMap", source.GetTextureScale(texture)); result.SetTextureOffset("_BaseMap", source.GetTextureOffset(texture)); }
            result.SetFloat("_Opacity", .48f); EditorUtility.SetDirty(result);
            ghostMaterials.Add(source, result); return result;
        }

        private static Material GroundMaterial(string name, int pattern, Color color)
        {
            var material = MaterialAt(name, "Defend/Placement/Ground");
            material.SetFloat("_Pattern", pattern); material.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(material); return material;
        }

        private static Material MaterialAt(string name, string shaderName)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new InvalidOperationException("缺少预览着色器：" + shaderName);
            string path = Root + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            else material.shader = shader;
            return material;
        }

        private static void ConfigureRange(LineRenderer line, Material material)
        {
            line.sharedMaterial = material; line.useWorldSpace = true; line.positionCount = 129;
            line.loop = false; line.textureMode = LineTextureMode.Stretch;
            line.startColor = line.endColor = Color.white;
            line.startWidth = line.endWidth = .025f;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        }

        private static LineRenderer CreateRangePrefab(Material material)
        {
            var root = new GameObject("已有中继器范围", typeof(LineRenderer));
            try
            {
                ConfigureRange(root.GetComponent<LineRenderer>(), material);
                return PrefabUtility.SaveAsPrefabAsset(root, Root + "/RelayRange.prefab").GetComponent<LineRenderer>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static GameObject CreateEfficiencyCell()
        {
            const string sourcePath = "Assets/Defend/Content/Prefabs/EnergyLossGridOverlay/EnergyLossGridOverlayCell.prefab";
            var cell = PrefabUtility.LoadPrefabContents(sourcePath);
            try
            {
                cell.GetComponentInChildren<Renderer>(true).sharedMaterial = GroundMaterial("EfficiencyCell", 1, Color.white);
                return PrefabUtility.SaveAsPrefabAsset(cell, Root + "/EfficiencyCell.prefab");
            }
            finally { PrefabUtility.UnloadPrefabContents(cell); }
        }

        private static void IntegrateLegend(Scene scene)
        {
            var controller = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<DefenseInterface>(true)).SingleOrDefault();
            if (controller == null) return;
            var data = new SerializedObject(controller);
            var parent = (RectTransform)data.FindProperty("mapViewport").objectReferenceValue;
            var old = (EnergyCoverageLegend)data.FindProperty("energyCoverageLegend").objectReferenceValue;
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var prefab = CreateLegendPrefab();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            controller.ConfigureCoverageLegend(instance.GetComponent<EnergyCoverageLegend>());
            EditorUtility.SetDirty(controller); PrefabUtility.RecordPrefabInstancePropertyModifications(controller);
        }

        private static GameObject CreateLegendPrefab()
        {
            var root = new GameObject("能源运输效率图例", typeof(RectTransform), typeof(Image), typeof(EnergyCoverageLegend));
            try
            {
                var panel = root.GetComponent<RectTransform>();
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0, 1);
                panel.anchoredPosition = new Vector2(16, -16); panel.sizeDelta = new Vector2(420, 199);
                root.GetComponent<Image>().color = new Color(.035f, .06f, .10f, .93f);
                Font font = AssetDatabase.LoadAssetAtPath<Font>(WhiteboxSceneBuilder.ChineseFontPath);
                LegendText("标题", panel, font, "已有覆盖与候选预览 · 运输效率", 16, 12, 388, 23, 17);
                var strip = new GameObject("效率色带", typeof(RectTransform), typeof(RawImage)).GetComponent<RectTransform>();
                strip.SetParent(panel, false); strip.anchorMin = strip.anchorMax = strip.pivot = new Vector2(0, 1);
                strip.anchoredPosition = new Vector2(30, -43); strip.sizeDelta = new Vector2(360, 9);
                strip.GetComponent<RawImage>().texture = CreateLegendTexture(); strip.GetComponent<RawImage>().raycastTarget = false;
                var labels = new Text[5];
                for (int i = 0; i < labels.Length; i++)
                {
                    labels[i] = LegendText("效率刻度" + i, strip, font, "100%", 0, 14, 50, 19, 13);
                    labels[i].rectTransform.pivot = new Vector2(.5f, 1); labels[i].alignment = TextAnchor.UpperCenter;
                }
                var point = LegendText("落点来源", panel, font, "将鼠标移到地图，查看落点运输效率", 16, 83, 390, 24, 15);
                var state = LegendText("来源当前状态", panel, font, "", 16, 109, 390, 23, 14);
                LegendText("填充说明", panel, font, "候选范围内显示候选效率，外围显示已有覆盖", 16, 142, 390, 22, 13);
                var note = LegendText("说明", panel, font, "按单次运输计算；范围覆盖不代表已经供电", 16, 167, 390, 22, 13);
                note.color = new Color(.65f, .75f, .83f);
                root.GetComponent<EnergyCoverageLegend>().Configure(strip, labels, point, state);
                root.SetActive(false);
                return PrefabUtility.SaveAsPrefabAsset(root, Root + "/EnergyCoverageLegend.prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Text LegendText(string name, Transform parent, Font font, string content, float x, float y, float width, float height, int size)
        {
            var text = new GameObject(name, typeof(RectTransform), typeof(Text)).GetComponent<Text>(); text.transform.SetParent(parent, false);
            var rect = text.rectTransform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
            text.font = font; text.fontSize = size; text.text = content; text.color = new Color(.90f, .95f, 1);
            text.alignment = TextAnchor.UpperLeft; text.raycastTarget = false;
            return text;
        }

        private static Texture2D CreateLegendTexture()
        {
            var material = GroundMaterial("EfficiencyCell", 1, Color.white);
            Color low = material.GetColor("_LowEfficiencyColor"), middle = material.GetColor("_MidEfficiencyColor"), high = material.GetColor("_HighEfficiencyColor");
            string path = Root + "/EfficiencyPalette.asset";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null) { texture = new Texture2D(256, 1, TextureFormat.RGBA32, false) { name = "运输效率色带" }; AssetDatabase.CreateAsset(texture, path); }
            for (int i = 0; i < 256; i++)
            {
                float efficiency = 1 - i / 255f;
                texture.SetPixel(i, 0, efficiency < .5f ? Color.Lerp(low, middle, efficiency * 2) : Color.Lerp(middle, high, (efficiency - .5f) * 2));
            }
            texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear; texture.Apply(); EditorUtility.SetDirty(texture);
            return texture;
        }

        private static Mesh FootprintMesh()
        {
            string path = Root + "/Footprint.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "摆放落点" };
            mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(-.5f,0,.5f), new Vector3(.5f,0,.5f), new Vector3(.5f,0,-.5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0,1,2,0,2,3 }; mesh.RecalculateNormals(); mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path); return mesh;
        }
    }
}
