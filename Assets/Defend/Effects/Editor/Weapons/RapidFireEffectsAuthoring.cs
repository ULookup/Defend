using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Defend.PhaseFiveEffects.Editor
{
    /// <summary>只制作速射炮的枪焰、接触火花与真实弹体外观，不保存场景或改动战斗配置。</summary>
    public static class RapidFireEffectsAuthoring
    {
        public const string ArtRoot = "Assets/Defend/Content/Art/RapidFirePolish";
        public const string ProjectilePath = "Assets/Defend/Content/PhaseFiveCombat/Impact/RapidProjectile.prefab";
        private const string EffectRoot = EffectAuthoring.RootPath + "/Prefabs/Weapons/";

        [MenuItem("涌现防线/美术/更新速射炮特效")]
        public static void BuildAssets()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请先结束播放再制作速射炮特效。");
            PrepareMaterials();
            BuildMuzzle(); BuildImpact(); BuildProjectileAsset();
            var library = AssetDatabase.LoadAssetAtPath<EffectLibrary>(EffectAuthoring.RootPath + "/EffectLibrary.asset");
            // 独立整包初次制作在本入口之后创建特效库；现役项目则立即修复组件引用。
            for (int index = 0; library != null && index < library.Entries.Length; index++)
            {
                string id = library.Entries[index].Id;
                if (id == "RapidMuzzle" || id == "RapidImpact")
                    library.Entries[index].Prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EffectRoot + id + ".prefab").GetComponent<PhaseFiveEffect>();
            }
            if (library != null) EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            Debug.Log("已保存速射炮的橙金枪焰、双层真实曳光弹与定向金属命中火花。");
        }

        private static void PrepareMaterials()
        {
            EffectAuthoring.Folder(ArtRoot + "/Materials"); EffectAuthoring.Folder(ArtRoot + "/Meshes");
            var shader = Shader.Find("Defend/RapidFire/LayeredEffect");
            if (shader == null) throw new InvalidOperationException("缺少速射炮特效着色器，请先完成资源导入。");
            Material("Flame", shader, new Color(2.2f, .68f, .08f, .75f), 0);
            Material("Glow", shader, new Color(2.5f, .8f, .12f, .85f), 1);
            Material("WhiteHeat", shader, new Color(3f, 2.3f, 1.2f, 1), 1);
            Material("TracerGlow", shader, new Color(2.2f, .82f, .14f, .85f), 2);
            Material("TracerCore", shader, new Color(3f, 2.7f, 1.8f, 1), 2);
            Material("Sparks", shader, new Color(2.7f, 1.3f, .3f, 1), 1);
            Material("Fragments", shader, new Color(.5f, .27f, .13f, .85f), 1, true);
            Material("Smoke", shader, new Color(.22f, .19f, .16f, .24f), 3, true);
            SaveMesh("FlameSheet", new[] { new Vector3(-.5f, 0, 0), new Vector3(.5f, 0, 0), new Vector3(-.5f, 0, 1), new Vector3(.5f, 0, 1) });
            SaveMesh("FlashSheet", new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) });
        }

        private static void Material(string name, Shader shader, Color color, int mode, bool alphaBlend = false)
        {
            string path = ArtRoot + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.shader = shader; material.SetColor("_BaseColor", color); material.SetFloat("_Mode", mode);
            material.SetFloat("_Opacity", 1); material.SetFloat("_Phase", 0);
            material.SetFloat("_DstBlend", (float)(alphaBlend ? BlendMode.OneMinusSrcAlpha : BlendMode.One));
            EditorUtility.SetDirty(material);
        }
        private static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(ArtRoot + "/Materials/" + name + ".mat");

        private static void SaveMesh(string name, Vector3[] vertices)
        {
            string path = ArtRoot + "/Meshes/" + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        }

        private static Transform Group(Transform parent, string name)
        { var group = new GameObject(name).transform; group.SetParent(parent, false); return group; }

        private static Renderer Sheet(Transform parent, string name, string mesh, string material, Vector3 scale, Quaternion rotation, Vector3 position)
        {
            var sheet = Group(parent, name); sheet.localScale = scale; sheet.localRotation = rotation; sheet.localPosition = position;
            sheet.gameObject.AddComponent<MeshFilter>().sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(ArtRoot + "/Meshes/" + mesh + ".asset");
            var renderer = sheet.gameObject.AddComponent<MeshRenderer>(); renderer.sharedMaterial = Material(material);
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private static void BuildMuzzle()
        {
            var effect = EffectAuthoring.Root<RapidMuzzleEffect>("RapidMuzzle", .25f);
            try
            {
                var flame = Group(effect.transform, "燃烧闪光");
                var renderers = new Renderer[4];
                for (int index = 0; index < 3; index++)
                    renderers[index] = Sheet(flame, "火焰轮廓" + (index + 1), "FlameSheet", "Flame",
                        new Vector3(.19f, 1, .38f), Quaternion.AngleAxis(index * 60f, Vector3.forward), Vector3.forward * .006f);
                renderers[3] = Sheet(flame, "白热枪口", "FlashSheet", "WhiteHeat", Vector3.one * .15f,
                    Quaternion.identity, Vector3.forward * .035f);
                var sparks = Particles(effect.transform, "枪口火星", "Sparks", 5, .06f, .13f, 2.1f, 4f, .012f, .023f, 13f, true);
                var gas = Particles(effect.transform, "短暂燃气", "Smoke", 2, .16f, .24f, .25f, .65f, .07f, .12f, 18f, false);
                var gasSize = gas.sizeOverLifetime; gasSize.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .5f, 1, 1.8f));
                effect.Configure(flame, renderers, sparks, gas);
                PrefabUtility.SaveAsPrefabAsset(effect.gameObject, EffectRoot + "RapidMuzzle.prefab");
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        private static void BuildImpact()
        {
            var effect = EffectAuthoring.Root<RapidImpactEffect>("RapidImpact", .32f);
            try
            {
                var contact = Group(effect.transform, "接触闪光");
                var renderers = new[]
                {
                    Sheet(contact, "橙金热辉", "FlashSheet", "Glow", Vector3.one * .3f, Quaternion.identity, Vector3.forward * .007f),
                    Sheet(contact, "白热接触点", "FlashSheet", "WhiteHeat", Vector3.one * .12f, Quaternion.identity, Vector3.forward * .01f),
                    Sheet(contact, "侧向接触闪光", "FlashSheet", "WhiteHeat", new Vector3(.1f, .16f, .1f), Quaternion.Euler(0, 90, 0), Vector3.forward * .025f)
                };
                var points = new Vector3[28];
                for (int index = 0; index < points.Length; index++)
                { float angle = index * Mathf.PI * 2f / points.Length; points[index] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), .004f); }
                var ring = EffectAuthoring.Line(effect.transform, "接触扩散", Material("TracerGlow"), .009f, points, true);
                var sparks = Particles(effect.transform, "反射金属火花", "Sparks", 8, .13f, .27f, 1.8f, 4.2f, .012f, .025f, 52f, true);
                var main = sparks.main; main.gravityModifier = .35f;
                var fragments = Particles(effect.transform, "金属碎屑", "Fragments", 3, .16f, .28f, .8f, 1.8f, .013f, .025f, 65f, true);
                main = fragments.main; main.gravityModifier = .7f;
                var smoke = Particles(effect.transform, "接触轻烟", "Smoke", 2, .18f, .3f, .15f, .35f, .08f, .13f, 48f, false);
                var smokeSize = smoke.sizeOverLifetime; smokeSize.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .6f, 1, 1.8f));
                effect.Configure(contact, renderers, ring, sparks, fragments, smoke);
                PrefabUtility.SaveAsPrefabAsset(effect.gameObject, EffectRoot + "RapidImpact.prefab");
            }
            finally { Object.DestroyImmediate(effect.gameObject); }
        }

        private static ParticleSystem Particles(Transform parent, string name, string material, int count,
            float minimumLife, float maximumLife, float minimumSpeed, float maximumSpeed,
            float minimumSize, float maximumSize, float coneAngle, bool stretched)
        {
            var particles = EffectAuthoring.Burst(parent, name, Material(material), Color.white,
                count, maximumSpeed, maximumSize, maximumLife);
            var main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minimumLife, maximumLife);
            main.startSpeed = new ParticleSystem.MinMaxCurve(minimumSpeed, maximumSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(minimumSize, maximumSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var shape = particles.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle; shape.radius = .016f;
            var colors = particles.colorOverLifetime;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(new Color(1, .45f, .14f), 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .04f), new GradientAlphaKey(.7f, .45f), new GradientAlphaKey(0, 1) });
            colors.color = gradient;
            if (stretched)
            {
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Stretch; renderer.velocityScale = .045f; renderer.lengthScale = 1.7f;
                renderer.cameraVelocityScale = 0;
            }
            return particles;
        }

        public static void BuildProjectileAsset()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePath);
            if (prefab == null) return; // 旧独立特效展台没有游戏弹体资源。
            var root = PrefabUtility.LoadPrefabContents(ProjectilePath);
            try
            {
                var glow = root.GetComponent<LineRenderer>();
                if (glow == null) throw new InvalidOperationException("真实速射弹体缺少既有 LineRenderer。");
                var child = root.transform.Find("白热弹芯");
                if (child == null) child = Group(root.transform, "白热弹芯");
                var core = child.GetComponent<LineRenderer>();
                if (core == null) core = child.gameObject.AddComponent<LineRenderer>();
                ConfigureLine(glow, "TracerGlow", .065f); ConfigureLine(core, "TracerCore", .021f);
                var visual = root.GetComponent<RapidProjectileVisual>();
                if (visual == null) visual = root.AddComponent<RapidProjectileVisual>();
                visual.Configure(glow, core); visual.SetVisible(false);
                PrefabUtility.SaveAsPrefabAsset(root, ProjectilePath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void ConfigureLine(LineRenderer line, string material, float width)
        {
            line.sharedMaterial = Material(material); line.useWorldSpace = true; line.positionCount = 2;
            line.SetPosition(0, Vector3.zero); line.SetPosition(1, Vector3.zero);
            line.widthMultiplier = width;
            line.widthCurve = new AnimationCurve(new Keyframe(0, .1f), new Keyframe(.68f, .8f), new Keyframe(1, 1));
            line.numCapVertices = 2; line.numCornerVertices = 0; line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            line.lightProbeUsage = LightProbeUsage.Off; line.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(1, .5f, .16f), 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(.55f, .3f), new GradientAlphaKey(1, .82f), new GradientAlphaKey(1, 1) });
            line.colorGradient = gradient;
        }
    }
}
