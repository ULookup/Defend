using UnityEngine;

namespace Defend.PhaseFiveEffects.Editor
{
    public static class WeaponEffectsAuthoring
    {
        public static void Build()
        {
            BuildMuzzle("RapidMuzzle", 0.12f, 0.06f, 0.028f, 4, false, false);
            BuildMuzzle("HeavyMuzzle", 0.32f, 0.10f, 0.070f, 7, true, false);
            BuildMuzzle("AreaDischarge", 0.18f, 0.11f, 0f, 6, false, true);
            BuildTracer("RapidTracer", 0.06f, 0.014f, 0.005f);
            BuildTracer("HeavyTracer", 0.09f, 0.028f, 0.010f);
            BuildImpact("RapidImpact", 0.14f, 5, 0.018f, 0.11f, 0.012f, 0.045f, false);
            BuildImpact("HeavyImpact", 0.28f, 9, 0.035f, 0.24f, 0.021f, 0.075f, false);
            BuildImpact("AreaImpact", 0.35f, 10, 0.07f, 0.68f, 0.022f, 0.065f, true);
            RapidFireEffectsAuthoring.BuildAssets();
        }

        private static void BuildMuzzle(string id, float lifetime, float flashSeconds,
            float recoil, int sparks, bool heavy, bool radial)
        {
            WeaponMuzzleEffect effect = EffectAuthoring.Root<WeaponMuzzleEffect>(id, lifetime);
            Transform flash = Group(effect.transform, "FlashGeometry");
            Material bright = EffectAuthoring.Material(radial ? "CyanGlow" : "WhiteGlow");
            Material warm = EffectAuthoring.Material(radial ? "CyanGlow" : "WarmGlow");
            if (radial)
            {
                EffectAuthoring.Line(flash, "DischargeRing", bright, 0.018f,
                    Circle(0.20f, 24, false), true);
                for (int index = 0; index < 6; index++)
                {
                    float angle = index * Mathf.PI / 3f;
                    Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    EffectAuthoring.Line(flash, "EmitterSegment" + index, bright, 0.014f,
                        new[] { direction * 0.09f, direction * 0.15f });
                }
            }
            else
            {
                float scale = heavy ? 1.6f : 1f;
                Transform core = EffectAuthoring.Shape(flash, "WhiteHotCore", PrimitiveType.Cube,
                    new Vector3(0, 0, 0.055f * scale),
                    new Vector3(0.025f, 0.025f, 0.105f) * scale, bright);
                core.localRotation = Quaternion.Euler(0, 0, 45);
                for (int index = 0; index < 2; index++)
                {
                    Transform flare = EffectAuthoring.Shape(flash, "WarmFlare" + index, PrimitiveType.Cube,
                        new Vector3(0, 0, 0.050f * scale),
                        new Vector3(0.012f, 0.092f, 0.055f) * scale, warm);
                    flare.localRotation = Quaternion.Euler(0, 0, 45 + index * 90);
                }
                if (heavy)
                    EffectAuthoring.Line(flash, "PressureRing", warm, 0.012f, Circle(0.075f, 24, true), true);
            }
            ParticleSystem particles = EffectAuthoring.Burst(effect.transform, "MuzzleSparks", warm,
                Color.white, sparks, heavy ? 1.35f : 0.8f, heavy ? 0.026f : 0.018f, flashSeconds);
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = radial ? 80f : 14f;
            shape.radius = radial ? 0.09f : 0.012f;
            if (radial) shape.rotation = new Vector3(-90, 0, 0);
            effect.Configure(flash, particles, sparks, flashSeconds, recoil, radial);
            EffectAuthoring.Save(effect, "Weapons", id);
        }

        private static void BuildTracer(string id, float lifetime, float outerWidth, float innerWidth)
        {
            WeaponTracerEffect effect = EffectAuthoring.Root<WeaponTracerEffect>(id, lifetime);
            LineRenderer outer = EffectAuthoring.Line(effect.transform, "WarmTrace",
                EffectAuthoring.Material("WarmGlow"), outerWidth, new[] { Vector3.zero, Vector3.forward });
            LineRenderer inner = EffectAuthoring.Line(effect.transform, "WhiteTrace",
                EffectAuthoring.Material("WhiteGlow"), innerWidth, new[] { Vector3.zero, Vector3.forward });
            effect.Configure(outer, inner, outerWidth, innerWidth);
            EffectAuthoring.Save(effect, "Weapons", id);
        }

        private static void BuildImpact(string id, float lifetime, int count, float startRadius,
            float endRadius, float width, float flashSeconds, bool area)
        {
            WeaponImpactEffect effect = EffectAuthoring.Root<WeaponImpactEffect>(id, lifetime);
            Material warm = EffectAuthoring.Material("WarmGlow");
            Material white = EffectAuthoring.Material("WhiteGlow");
            Transform contact = Group(effect.transform, "ContactFlash");
            float contactSize = area ? 0.08f : Mathf.Min(0.085f, endRadius * 0.4f);
            Transform center = EffectAuthoring.Shape(contact, "ContactCore", PrimitiveType.Cube,
                new Vector3(0, 0.035f, 0), new Vector3(contactSize, contactSize, contactSize), white);
            center.localRotation = Quaternion.Euler(20, 45, 15);
            Vector3[] rays = new Vector3[16];
            for (int index = 0; index < 4; index++)
            {
                float angle = index * Mathf.PI * 0.5f + 0.35f;
                Vector3 direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                rays[index * 4] = Vector3.up * 0.02f;
                rays[index * 4 + 1] = direction * contactSize * 1.7f + Vector3.up * 0.02f;
                rays[index * 4 + 2] = direction * contactSize * 2.3f + Vector3.up * 0.02f;
                rays[index * 4 + 3] = Vector3.up * 0.02f;
            }
            EffectAuthoring.Line(contact, "ContactRays", warm, area ? 0.012f : 0.009f, rays);
            LineRenderer ring = EffectAuthoring.Line(effect.transform, "ExpandingImpactRing", warm,
                width, Circle(1f, area ? 40 : 24, false), true);
            ring.transform.localPosition = Vector3.up * 0.014f;
            ParticleSystem particles = EffectAuthoring.Burst(effect.transform, "ImpactSparks", warm,
                Color.white, count, area ? 0.65f : 0.95f, area ? 0.022f : 0.019f,
                Mathf.Min(lifetime, 0.19f));
            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = area ? 65f : 42f;
            shape.radius = area ? 0.13f : 0.015f;
            shape.rotation = new Vector3(-90, 0, 0);
            ParticleSystem.MainModule main = particles.main;
            main.gravityModifier = 0.18f;
            effect.Configure(contact, ring, particles, count, startRadius, endRadius, width, flashSeconds);
            EffectAuthoring.Save(effect, "Weapons", id);
        }

        private static Transform Group(Transform parent, string name)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static Vector3[] Circle(float radius, int segments, bool faceForward)
        {
            Vector3[] points = new Vector3[segments];
            for (int index = 0; index < segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                points[index] = faceForward
                    ? new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0.025f) * radius
                    : new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
            }
            return points;
        }
    }
}
