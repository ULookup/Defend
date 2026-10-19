using System.Collections.Generic;
using UnityEngine;

namespace Defend.PhaseFiveEffects.Editor
{
    public static class EnergyEffectsAuthoring
    {
        public static void Build()
        {
            BuildStatus("ReactorRunning", EnergyStatusKind.Reactor, 1.05f);
            BuildStatus("RelayRunning", EnergyStatusKind.Relay, 1.5f);
            BuildEndpoint("PortSend", false, .16f);
            BuildTransfer();
            BuildEndpoint("EnergyArrival", true, .24f);
            BuildStatus("EnergyShortage", EnergyStatusKind.Shortage, 1.2f);
            BuildStatus("EnergyDisconnected", EnergyStatusKind.Disconnected, 1.4f);
            BuildStatus("HeavyLock", EnergyStatusKind.HeavyLock, 1f);
            BuildStatus("SearchMarker", EnergyStatusKind.Search, 1.6f);
            BuildShield("ShieldHit", false, .22f);
            BuildShield("ShieldBreak", true, .50f);
        }

        private static void BuildStatus(string id, EnergyStatusKind kind, float period)
        {
            EnergyStatusEffect effect = EffectAuthoring.Root<EnergyStatusEffect>(id, period, EffectBudgetGroup.Status);
            effect.Looping = true;
            Transform geometry = Group(effect.transform, "StatusGeometry");
            List<LineRenderer> lines = new List<LineRenderer>();
            Material cyan = EffectAuthoring.Material("CyanGlow");
            Material amber = EffectAuthoring.Material("AmberGlow");
            if (kind == EnergyStatusKind.Reactor)
            {
                for (int index = 0; index < 3; index++)
                {
                    float height = .025f + index * .035f;
                    float depth = -.045f + index * .045f;
                    lines.Add(EffectAuthoring.Line(geometry, "EnergyIndicator" + index, cyan, .021f,
                        new[] { new Vector3(-.065f, height, depth), new Vector3(.065f, height, depth) }));
                }
            }
            else if (kind == EnergyStatusKind.Relay)
            {
                for (int index = 0; index < 3; index++)
                    lines.Add(EffectAuthoring.Line(geometry, "RelayArc" + index, cyan, .014f,
                        Arc(.155f, index * 120f, index * 120f + 80f, 7)));
            }
            else if (kind == EnergyStatusKind.Shortage)
            {
                lines.Add(EffectAuthoring.Line(geometry, "BrokenEnergyRing", amber, .025f, Arc(.245f, 48, 312, 25)));
                lines.Add(EffectAuthoring.Line(geometry, "RemainingEnergyBar", amber, .018f,
                    new[] { new Vector3(-.10f, .025f, -.075f), new Vector3(-.10f, .025f, .075f) }));
            }
            else if (kind == EnergyStatusKind.Disconnected)
            {
                lines.Add(EffectAuthoring.Line(geometry, "DisconnectedLeft", amber, .023f,
                    new[] { new Vector3(-.25f, .025f, 0), new Vector3(-.07f, .025f, 0) }));
                lines.Add(EffectAuthoring.Line(geometry, "DisconnectedRight", amber, .023f,
                    new[] { new Vector3(.07f, .025f, 0), new Vector3(.25f, .025f, 0) }));
                lines.Add(EffectAuthoring.Line(geometry, "DisconnectionSlash", amber, .023f,
                    new[] { new Vector3(-.08f, .025f, -.14f), new Vector3(.08f, .025f, .14f) }));
            }
            else if (kind == EnergyStatusKind.HeavyLock)
            {
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        lines.Add(EffectAuthoring.Line(geometry, "LockCorner" + x + "_" + z,
                            EffectAuthoring.Material("WarmGlow"), .024f,
                            new[] { new Vector3(x * .14f, .025f, z * .275f),
                                    new Vector3(x * .275f, .025f, z * .275f),
                                    new Vector3(x * .275f, .025f, z * .14f) }));
            }
            else
            {
                for (int arm = 0; arm < 4; arm++)
                {
                    Vector3 direction = Quaternion.Euler(0, arm * 90f, 0) * Vector3.forward;
                    for (int dash = 0; dash < 2; dash++)
                    {
                        float start = .075f + dash * .10f;
                        lines.Add(EffectAuthoring.Line(geometry, "SearchDash" + arm + "_" + dash,
                            amber, .018f, new[] { direction * start + Vector3.up * .025f,
                                direction * (start + .055f) + Vector3.up * .025f }));
                    }
                }
            }
            effect.Configure(kind, geometry, lines.ToArray());
            EffectAuthoring.Save(effect, "Energy", id);
        }

        private static void BuildEndpoint(string id, bool arrival, float lifetime)
        {
            EnergyTransferPolishAuthoring.BuildEndpoint(id, arrival, lifetime);
        }

        private static void BuildTransfer()
        {
            EnergyTransferPolishAuthoring.BuildTransfer();
        }

        private static void BuildShield(string id, bool breaking, float lifetime)
        {
            EnergyShieldEffect effect = EffectAuthoring.Root<EnergyShieldEffect>(id, lifetime);
            Transform geometry = Group(effect.transform, "LocalShieldGeometry");
            List<LineRenderer> lines = new List<LineRenderer>();
            List<Vector3> directions = new List<Vector3>();
            Material shield = EffectAuthoring.Material("ShieldCyan");
            if (breaking)
            {
                for (int index = 0; index < 6; index++)
                {
                    float angle = index * 60f;
                    lines.Add(EffectAuthoring.Line(geometry, "BrokenShieldSegment" + index,
                        shield, .022f, Arc(.225f, angle + 6f, angle + 49f, 4)));
                    directions.Add(new Vector3(Mathf.Cos((angle + 27.5f) * Mathf.Deg2Rad), .18f,
                        Mathf.Sin((angle + 27.5f) * Mathf.Deg2Rad)));
                }
            }
            else
            {
                lines.Add(EffectAuthoring.Line(geometry, "ShieldContactHexagon", shield, .019f,
                    Arc(.095f, 0, 300, 6), true));
                lines.Add(EffectAuthoring.Line(geometry, "ShieldContactCrescent", shield, .017f,
                    Arc(.175f, 20, 150, 12)));
                lines.Add(EffectAuthoring.Line(geometry, "ShieldOuterCrescent", shield, .012f,
                    Arc(.225f, 37, 130, 10)));
            }
            int count = breaking ? 8 : 4;
            ParticleSystem splinters = EffectAuthoring.Burst(effect.transform, "ShieldSplinters",
                EffectAuthoring.Material("CyanGlow"), new Color(.65f, 1f, 1f, .85f), count,
                breaking ? .58f : .30f, breaking ? .027f : .018f, lifetime * .8f);
            ParticleSystem.ShapeModule shape = splinters.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = breaking ? 74f : 55f;
            shape.radius = breaking ? .12f : .025f;
            shape.rotation = new Vector3(-90, 0, 0);
            effect.Configure(breaking, geometry, lines.ToArray(), directions.ToArray(), splinters, count);
            EffectAuthoring.Save(effect, "Energy", id);
        }

        private static Transform Group(Transform parent, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static Vector3[] Arc(float radius, float startDegrees, float endDegrees, int points)
        {
            Vector3[] result = new Vector3[points];
            for (int index = 0; index < points; index++)
            {
                float angle = Mathf.Lerp(startDegrees, endDegrees, index / (float)(points - 1)) * Mathf.Deg2Rad;
                result[index] = new Vector3(Mathf.Cos(angle) * radius, .025f, Mathf.Sin(angle) * radius);
            }
            return result;
        }
    }
}
