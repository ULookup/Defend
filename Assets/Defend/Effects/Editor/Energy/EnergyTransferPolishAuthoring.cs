using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Defend.PhaseFiveEffects.Editor
{
    /// <summary>Updates only the three transport effects while preserving their library identifiers and asset GUIDs.</summary>
    public static class EnergyTransferPolishAuthoring
    {
        public const string ArtPath = "Assets/Defend/Content/Art/EnergyTransferPolish";

        [MenuItem("Defend/Art/Build Energy Transfer Polish")]
        public static void BuildAssets()
        {
            PrepareResources();
            BuildTransferGeometry();
            BuildEndpointGeometry("PortSend", false, .16f);
            BuildEndpointGeometry("EnergyArrival", true, .24f);
            AssetDatabase.SaveAssets();
        }

        public static void BuildTransfer()
        {
            PrepareResources();
            BuildTransferGeometry();
        }

        public static void BuildEndpoint(string id, bool arrival, float lifetime)
        {
            if (id != "PortSend" && id != "EnergyArrival")
                throw new ArgumentException("Only transport departure and arrival effects belong to this authoring entry.", nameof(id));
            PrepareResources();
            BuildEndpointGeometry(id, arrival, lifetime);
        }

        private static void PrepareResources()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("请先结束播放再制作能源运输特效。");
            EffectAuthoring.Folder(ArtPath);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(ArtPath + "/EnergyTransferSurface.shader");
            if (shader == null) throw new InvalidOperationException("Energy transfer shader has not imported.");
            SaveMaterial("CrystalShell", shader, new Color(.055f, .82f, 1.3f, .8f), 0);
            SaveMaterial("CrystalCore", shader, new Color(.72f, 1.18f, 1.35f, .95f), 1);
            SaveMaterial("TrailCore", shader, new Color(.42f, 1.1f, 1.3f, 1f), 2);
            SaveMaterial("TrailGlow", shader, new Color(.035f, .66f, 1.2f, .75f), 2);
            SaveMaterial("EndpointGlow", shader, new Color(.045f, .8f, 1.25f, .7f), 3);
            SaveMesh("EnergyCrystal", CreateCrystalMesh());
            SaveMesh("EndpointDisc", CreateDiscMesh());
        }

        private static void BuildTransferGeometry()
        {
            EnergyTransferEffect effect = EffectAuthoring.Root<EnergyTransferEffect>(
                "EnergyTransfer", .45f, EffectBudgetGroup.EnergyPulse);
            Transform head = Group(effect.transform, "EnergyCrystal");
            Mesh crystal = AssetDatabase.LoadAssetAtPath<Mesh>(ArtPath + "/EnergyCrystal.asset");
            Renderer shell = MeshObject(head, "CrystalShell", crystal, Material("CrystalShell"),
                new Vector3(.15f, .12f, .19f));
            Renderer core = MeshObject(head, "CrystalCore", crystal, Material("CrystalCore"),
                new Vector3(.074f, .064f, .12f));
            LineRenderer coreTrail = Trail(effect.transform, "EnergyCoreTrail", Material("TrailCore"), .018f);
            LineRenderer glowTrail = Trail(effect.transform, "EnergyGlowTrail", Material("TrailGlow"), .058f);
            effect.ConfigurePolish(head, coreTrail, glowTrail, new[] { shell, core });
            EffectAuthoring.Save(effect, "Energy", "EnergyTransfer");
        }

        private static void BuildEndpointGeometry(string id, bool arrival, float lifetime)
        {
            EnergyEndpointEffect effect = EffectAuthoring.Root<EnergyEndpointEffect>(id, lifetime);
            Transform geometry = Group(effect.transform, "EndpointGeometry");
            List<LineRenderer> arcs = new List<LineRenderer>(3);
            float radius = arrival ? .16f : .12f;
            for (int segment = 0; segment < 3; segment++)
            {
                Vector3[] points = new Vector3[9];
                for (int point = 0; point < points.Length; point++)
                {
                    float angle = (segment * 120f + 18f + point * 10f) * Mathf.Deg2Rad;
                    points[point] = new Vector3(Mathf.Cos(angle) * radius, .014f, Mathf.Sin(angle) * radius);
                }
                LineRenderer arc = EffectAuthoring.Line(geometry, "EnergyPortArc" + segment,
                    Material("TrailCore"), .018f, points);
                arc.generateLightingData = true;
                arcs.Add(arc);
            }
            Renderer glow = MeshObject(geometry, "EnergyPortGlow",
                AssetDatabase.LoadAssetAtPath<Mesh>(ArtPath + "/EndpointDisc.asset"), Material("EndpointGlow"),
                new Vector3(radius * 3.6f, 1, radius * 3.6f));
            glow.transform.localPosition = new Vector3(0, .009f, 0);
            effect.ConfigurePolish(arrival, geometry, arcs.ToArray(), glow);
            EffectAuthoring.Save(effect, "Energy", id);
        }

        private static LineRenderer Trail(Transform parent, string name, Material material, float width)
        {
            LineRenderer line = EffectAuthoring.Line(parent, name, material, width,
                new[] { Vector3.zero, Vector3.zero });
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.generateLightingData = true;
            line.numCapVertices = 3;
            line.widthCurve = new AnimationCurve(new Keyframe(0, .04f), new Keyframe(.55f, .6f), new Keyframe(1, 1));
            return line;
        }

        private static Renderer MeshObject(Transform parent, string name, Mesh mesh, Material material, Vector3 scale)
        {
            Transform visual = Group(parent, name);
            visual.localScale = scale;
            visual.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }

        private static Transform Group(Transform parent, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(ArtPath + "/" + name + ".mat");

        private static void SaveMaterial(string name, Shader shader, Color color, int surfaceKind)
        {
            string path = ArtPath + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_SurfaceKind", surfaceKind);
            material.SetFloat("_Pulse", 1);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            EditorUtility.SetDirty(material);
        }

        private static void SaveMesh(string name, Mesh mesh)
        {
            string path = ArtPath + "/" + name + ".asset";
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            mesh.name = name;
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                UnityEngine.Object.DestroyImmediate(mesh);
                EditorUtility.SetDirty(existing);
            }
        }

        private static Mesh CreateDiscMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = new[] { new Vector3(-.5f,0,-.5f), new Vector3(-.5f,0,.5f),
                new Vector3(.5f,0,.5f), new Vector3(.5f,0,-.5f) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateCrystalMesh()
        {
            const float bevel = .34f;
            List<Vector3> vertices = new List<Vector3>(96);
            List<Vector3> normals = new List<Vector3>(96);
            List<Vector2> uv = new List<Vector2>(96);
            List<Color> colors = new List<Color>(96);
            List<int> triangles = new List<int>(132);
            void Face(params Vector3[] points)
            {
                Vector3 normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                Vector3 center = Vector3.zero;
                foreach (Vector3 point in points) center += point;
                if (Vector3.Dot(normal, center) < 0) { Array.Reverse(points); normal = -normal; }
                int first = vertices.Count;
                for (int index = 0; index < points.Length; index++)
                {
                    vertices.Add(points[index]); normals.Add(normal); uv.Add(Vector2.zero); colors.Add(Color.white);
                }
                for (int index = 1; index < points.Length - 1; index++)
                { triangles.Add(first); triangles.Add(first + index); triangles.Add(first + index + 1); }
            }
            for (int axis = 0; axis < 3; axis++)
            {
                int second = (axis + 1) % 3, third = (axis + 2) % 3;
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3[] points = new Vector3[4];
                    for (int index = 0; index < 4; index++)
                    {
                        Vector3 point = Vector3.zero; point[axis] = sign * .5f;
                        point[second] = (index == 0 || index == 3 ? -1 : 1) * bevel;
                        point[third] = (index < 2 ? -1 : 1) * bevel; points[index] = point;
                    }
                    Face(points);
                }
                for (int firstSign = -1; firstSign <= 1; firstSign += 2)
                for (int secondSign = -1; secondSign <= 1; secondSign += 2)
                {
                    Vector3 a = Vector3.zero; a[axis] = firstSign * .5f; a[second] = secondSign * bevel; a[third] = -bevel;
                    Vector3 b = Vector3.zero; b[axis] = firstSign * bevel; b[second] = secondSign * .5f; b[third] = -bevel;
                    Vector3 c = b; c[third] = bevel;
                    Vector3 d = a; d[third] = bevel;
                    Face(a,b,c,d);
                }
            }
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                Face(new Vector3(x * .5f, y * bevel, z * bevel),
                    new Vector3(x * bevel, y * .5f, z * bevel), new Vector3(x * bevel, y * bevel, z * .5f));
            Mesh mesh = new Mesh();
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0, uv); mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
