using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SkyCity.MonsterGate
{
    /// <summary>
    /// Presentation only. A wave director calls WarnNextWave(), Open(), Close().
    /// Import the RUNTIME/CLOSED-pose model, with all FX transform scales equal to one.
    /// Do not run an Animator that also writes these door/FX transforms.
    /// Compatible API target: Unity 2022.3; no Input System/package dependency.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Sky City/Monster Gate Controller")]
    public sealed class MonsterGateController : MonoBehaviour
    {
        public enum GateState { Closed = 0, Open = 1, Warning = 2 }

        [Header("Startup")]
        [SerializeField] private GateState initialState = GateState.Closed;
        [SerializeField] private bool autoBindOnAwake = true;

        [Header("Hierarchy (auto binding is available in the component menu)")]
        [Tooltip("Coordinate space for the opening X axis. Normally the prefab wrapper.")]
        [SerializeField] private Transform coordinateRoot;
        [SerializeField] private Transform gateLeafL;
        [SerializeField] private Transform gateLeafR;
        [SerializeField] private Renderer portalSurface;
        [SerializeField] private Transform fxOrbit;
        [SerializeField] private Renderer[] energyRenderers = new Renderer[0];
        [SerializeField] private Renderer[] warningSegments = new Renderer[0];
        [SerializeField] private Renderer[] prechargeRenderers = new Renderer[0];
        [SerializeField] private Renderer[] orbitRenderers = new Renderer[0];
        [SerializeField] private Renderer[] colliderProxyRenderers = new Renderer[0];

        [Header("Motion")]
        [Tooltip("Disable if an Animator owns the leaf transforms. State materials still work.")]
        [SerializeField] private bool driveDoorTransforms = true;
        [SerializeField, Min(0f)] private float openTravel = 1.27f;
        [SerializeField] private Vector3 openingAxisInRoot = Vector3.right;
        [SerializeField, Min(0f)] private float transitionSeconds = 1.2f;
        [Tooltip("Off by default: the tall orbit silhouette remains fixed while the shader scrolls.")]
        [SerializeField] private bool driveOrbitTransform;
        [Tooltip("Axis in coordinateRoot space. Unity front-facing plane normal is Z.")]
        [SerializeField] private Vector3 orbitAxisInRoot = Vector3.forward;
        [SerializeField] private float orbitDegreesPerSecond = 22f;

        [Header("Energy")]
        [ColorUsage(false, true)]
        [SerializeField] private Color openColor = new Color(0.004f, 0.410f, 0.930f, 1f);
        [ColorUsage(false, true)]
        [SerializeField] private Color warningColor = new Color(1f, 0.38f, 0.035f, 1f);
        [SerializeField, Min(0f)] private float standbyEmission = 0.18f;
        [SerializeField, Min(0f)] private float openEmission = 3.2f;
        [SerializeField, Min(0f)] private float warningEmission = 4.0f;
        [SerializeField, Range(0.1f, 4f)] private float warningPulseHz = 1.35f;
        [SerializeField, Range(0.1f, 3f)] private float warningSweepHz = 0.55f;
        [SerializeField, Range(0.01f, 1f)] private float portalTurnsPerSecond = 0.075f;

        [Header("Completed stable-state changes (startup does not invoke these)")]
        [SerializeField] private UnityEvent onClosed = new UnityEvent();
        [SerializeField] private UnityEvent onOpened = new UnityEvent();
        [SerializeField] private UnityEvent onWarning = new UnityEvent();

        public GateState TargetState { get { return initialized ? targetState : initialState; } }
        public GateState LastCompletedState { get { return initialized ? completedState : initialState; } }
        public bool IsTransitioning { get { return transitioning; } }
        public float OpenAmount { get { return openAmount; } }
        public UnityEvent OnClosed { get { return onClosed; } }
        public UnityEvent OnOpened { get { return onOpened; } }
        public UnityEvent OnWarning { get { return onWarning; } }

        private enum Channel { Energy, Portal, Warning, Precharge, Orbit }
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int VisibilityId = Shader.PropertyToID("_GateVisibility");
        private static readonly int PhaseId = Shader.PropertyToID("_GatePhase");

        private sealed class Binding
        {
            public Renderer Renderer;
            public int MaterialSlot;
            public Channel Channel;
            public int SequenceIndex;
            public int SequenceCount;
            public float Gain;
            public MaterialPropertyBlock Original;
            public MaterialPropertyBlock Working;
        }

        private struct RendererSnapshot
        {
            public Renderer Renderer;
            public bool Enabled;
        }

        private Binding[] bindings = new Binding[0];
        private RendererSnapshot[] rendererSnapshots = new RendererSnapshot[0];
        private bool initialized;
        private bool transitioning;
        private GateState targetState;
        private GateState completedState;
        private Vector3 leafClosedL;
        private Vector3 leafClosedR;
        private Quaternion orbitRestRotation;
        private float openAmount;
        private float warningAmount;
        private float fromOpen;
        private float fromWarning;
        private float elapsed;
        private float pulsePhase;
        private float sweepPhase;
        private float portalPhase;
        private float orbitAngle;

        private void Awake() { Initialize(); }

        private void OnEnable()
        {
            Initialize();
            ApplyVisuals();
        }

        // Disabling this component deliberately freezes the current pose and material phase.
        // No coroutine continues while disabled. Re-enabling resumes the pending transition.
        // Disabling the complete GameObject also naturally hides its hierarchy.

        /// <summary>Advance with the existing presentation clock difference; zero freezes all effects.</summary>
        public void Advance(float dt)
        {
            if (!isActiveAndEnabled) return;
            Initialize();
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) dt = 0f;
            pulsePhase = Mathf.Repeat(pulsePhase + dt * warningPulseHz, 1f);
            sweepPhase = Mathf.Repeat(sweepPhase + dt * warningSweepHz, 1f);
            portalPhase = Mathf.Repeat(portalPhase + dt * portalTurnsPerSecond, 1f);
            orbitAngle = Mathf.Repeat(orbitAngle + dt * orbitDegreesPerSecond * openAmount, 360f);

            bool completedNow = false;
            if (transitioning)
            {
                elapsed += dt;
                float t = transitionSeconds <= 0f ? 1f : Mathf.Clamp01(elapsed / transitionSeconds);
                float eased = t * t * (3f - 2f * t);
                openAmount = Mathf.Lerp(fromOpen, targetState == GateState.Open ? 1f : 0f, eased);
                warningAmount = Mathf.Lerp(fromWarning, targetState == GateState.Warning ? 1f : 0f, eased);
                completedNow = t >= 1f;
                if (completedNow) transitioning = false;
            }

            ApplyVisuals();
            // Notify last: a listener may synchronously request another state.
            if (completedNow) NotifyCompleted();
        }

        public void Close() { SetState(GateState.Closed); }
        public void Open() { SetState(GateState.Open); }
        public void WarnNextWave() { SetState(GateState.Warning); }
        public void CloseImmediately() { SetState(GateState.Closed, true); }
        public void OpenImmediately() { SetState(GateState.Open, true); }
        public void WarnImmediately() { SetState(GateState.Warning, true); }

        /// <summary>Reset a reused entrance or a replacement world without completion callbacks.</summary>
        public void ResetVisualState(GateState state = GateState.Closed)
        {
            if ((uint)state > 2u) return;
            Initialize();
            targetState = completedState = state;
            transitioning = false;
            elapsed = pulsePhase = sweepPhase = portalPhase = orbitAngle = 0f;
            openAmount = fromOpen = state == GateState.Open ? 1f : 0f;
            warningAmount = fromWarning = state == GateState.Warning ? 1f : 0f;
            ApplyVisuals();
        }

        /// <summary>Useful for an Inspector UnityEvent with an integer argument: 0/1/2.</summary>
        public void SetStateFromIndex(int state)
        {
            if (state < 0 || state > 2) return;
            SetState((GateState)state);
        }

        /// <summary>
        /// Same-state requests are idempotent. An interrupted transition starts at the current
        /// interpolated pose. Only states actually reached produce a completion event.
        /// Warning remains closed until the wave system explicitly requests Open.
        /// </summary>
        public void SetState(GateState state, bool immediate = false)
        {
            if ((uint)state > 2u) return;
            Initialize();
            if (state == targetState && (!immediate || !transitioning)) return;

            fromOpen = openAmount;
            fromWarning = warningAmount;
            targetState = state;
            elapsed = 0f;
            transitioning = !immediate && transitionSeconds > 0f;
            if (!transitioning)
            {
                openAmount = state == GateState.Open ? 1f : 0f;
                warningAmount = state == GateState.Warning ? 1f : 0f;
                ApplyVisuals();
                NotifyCompleted();
            }
        }

        private void NotifyCompleted()
        {
            if (completedState == targetState) return;
            completedState = targetState;
            // All controller state is committed before invoking the single matching event.
            // Therefore a re-entrant SetState() call from a listener is not overwritten.
            switch (completedState)
            {
                case GateState.Closed: onClosed.Invoke(); break;
                case GateState.Open: onOpened.Invoke(); break;
                case GateState.Warning: onWarning.Invoke(); break;
            }
        }

        private void Initialize()
        {
            if (initialized) return;
            if (autoBindOnAwake) AutoBindMissingReferences();
            if (!coordinateRoot) coordinateRoot = transform;
            if (gateLeafL) leafClosedL = gateLeafL.localPosition;
            if (gateLeafR) leafClosedR = gateLeafR.localPosition;
            if (fxOrbit) orbitRestRotation = fxOrbit.localRotation;
            CaptureMaterials();
            targetState = completedState = initialState;
            openAmount = initialState == GateState.Open ? 1f : 0f;
            warningAmount = initialState == GateState.Warning ? 1f : 0f;
            initialized = true;
            ApplyVisuals();
        }

        private void ApplyVisuals()
        {
            if (!initialized) return;
            if (driveDoorTransforms)
            {
                Vector3 axis = openingAxisInRoot.sqrMagnitude > 0.0001f
                    ? openingAxisInRoot.normalized : Vector3.right;
                Vector3 offsetWorld = coordinateRoot.TransformVector(axis * openTravel * openAmount);
                ApplyLeaf(gateLeafL, leafClosedL, -offsetWorld);
                ApplyLeaf(gateLeafR, leafClosedR, offsetWorld);
            }

            if (driveOrbitTransform && fxOrbit)
            {
                Vector3 axis = orbitAxisInRoot.sqrMagnitude > 0.0001f
                    ? orbitAxisInRoot.normalized : Vector3.forward;
                Vector3 worldAxis = coordinateRoot.TransformDirection(axis);
                Vector3 parentAxis = fxOrbit.parent
                    ? fxOrbit.parent.InverseTransformDirection(worldAxis) : worldAxis;
                fxOrbit.localRotation = Quaternion.AngleAxis(orbitAngle, parentAxis) * orbitRestRotation;
            }

            float pulse = 0.5f + 0.5f * Mathf.Sin(pulsePhase * Mathf.PI * 2f);
            float energyIntensity = standbyEmission + openAmount * (openEmission - standbyEmission)
                + warningAmount * warningEmission * Mathf.Lerp(0.35f, 1f, pulse);
            // Main conduits stay cyan in all states; amber is reserved for warning segments.
            Color energyColor = openColor;

            SetRendererEnabled(portalSurface, openAmount > 0.001f);
            SetGroupEnabled(orbitRenderers, openAmount > 0.001f);
            SetGroupEnabled(prechargeRenderers, warningAmount > 0.001f);
            SetGroupEnabled(warningSegments, warningAmount > 0.001f);
            SetGroupEnabled(colliderProxyRenderers, false);

            for (int i = 0; i < bindings.Length; ++i)
            {
                Binding binding = bindings[i];
                if (!binding.Renderer) continue;
                Color color = energyColor;
                float intensity = energyIntensity;
                float visibility = 1f;
                switch (binding.Channel)
                {
                    case Channel.Portal:
                        color = openColor;
                        intensity = openEmission;
                        visibility = openAmount;
                        break;
                    case Channel.Orbit:
                        color = openColor;
                        intensity = openEmission * openAmount;
                        visibility = openAmount;
                        break;
                    case Channel.Precharge:
                        color = openColor;
                        intensity = warningAmount * Mathf.Lerp(0.65f, 2.5f, pulse);
                        visibility = warningAmount;
                        break;
                    case Channel.Warning:
                        float offset = (float)binding.SequenceIndex / Mathf.Max(1, binding.SequenceCount);
                        float distance = Mathf.Abs(sweepPhase - offset);
                        distance = Mathf.Min(distance, 1f - distance);
                        float chase = Mathf.Clamp01(1f - distance * 5f);
                        color = warningColor;
                        intensity = warningAmount * warningEmission * (0.2f + 0.8f * chase) * (0.65f + pulse * 0.35f);
                        visibility = warningAmount;
                        break;
                }
                ApplyProperties(binding, color, intensity * binding.Gain, visibility);
            }
        }

        private static void ApplyLeaf(Transform leaf, Vector3 closedPosition, Vector3 worldOffset)
        {
            if (!leaf) return;
            Vector3 parentOffset = leaf.parent ? leaf.parent.InverseTransformVector(worldOffset) : worldOffset;
            leaf.localPosition = closedPosition + parentOffset;
        }

        private void ApplyProperties(Binding binding, Color color, float intensity, float visibility)
        {
            // Read the latest block to preserve unrelated per-slot properties from other effects.
            // This controller exclusively owns the five property IDs listed above.
            binding.Renderer.GetPropertyBlock(binding.Working, binding.MaterialSlot);
            if (binding.Working.isEmpty) binding.Renderer.GetPropertyBlock(binding.Working);
            Color baseColor = color * visibility;
            baseColor.a = visibility;
            Color emission = color * intensity;
            emission.a = 1f;
            binding.Working.SetColor(BaseColorId, baseColor);
            binding.Working.SetColor(ColorId, baseColor);
            binding.Working.SetColor(EmissionId, emission);
            binding.Working.SetFloat(VisibilityId, visibility);
            binding.Working.SetFloat(PhaseId, portalPhase);
            binding.Renderer.SetPropertyBlock(binding.Working, binding.MaterialSlot);
        }

        private static void SetRendererEnabled(Renderer renderer, bool value)
        {
            if (renderer && renderer.enabled != value) renderer.enabled = value;
        }

        private static void SetGroupEnabled(Renderer[] renderers, bool value)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; ++i) SetRendererEnabled(renderers[i], value);
        }

        private void CaptureMaterials()
        {
            var result = new List<Binding>();
            var snapshots = new List<RendererSnapshot>();
            var seen = new HashSet<Renderer>();
            // Specific FX channels take priority over broad emission discovery.
            AddRenderer(portalSurface, Channel.Portal, 0, 1, result, snapshots, seen);
            AddGroup(warningSegments, Channel.Warning, result, snapshots, seen);
            AddGroup(prechargeRenderers, Channel.Precharge, result, snapshots, seen);
            AddGroup(orbitRenderers, Channel.Orbit, result, snapshots, seen);
            AddGroup(energyRenderers, Channel.Energy, result, snapshots, seen);
            if (colliderProxyRenderers != null)
            {
                for (int i = 0; i < colliderProxyRenderers.Length; ++i)
                {
                    Renderer renderer = colliderProxyRenderers[i];
                    if (renderer && seen.Add(renderer))
                        snapshots.Add(new RendererSnapshot { Renderer = renderer, Enabled = renderer.enabled });
                }
            }
            bindings = result.ToArray();
            rendererSnapshots = snapshots.ToArray();
        }

        private static void AddGroup(Renderer[] renderers, Channel channel, List<Binding> result,
            List<RendererSnapshot> snapshots, HashSet<Renderer> seen)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; ++i)
                AddRenderer(renderers[i], channel, i, renderers.Length, result, snapshots, seen);
        }

        private static void AddRenderer(Renderer renderer, Channel channel, int index, int count,
            List<Binding> result, List<RendererSnapshot> snapshots, HashSet<Renderer> seen)
        {
            if (!renderer || !seen.Add(renderer)) return;
            snapshots.Add(new RendererSnapshot { Renderer = renderer, Enabled = renderer.enabled });
            Material[] materials = renderer.sharedMaterials; // Allocates only during initialization.
            for (int slot = 0; slot < materials.Length; ++slot)
            {
                Material material = materials[slot];
                if (!material) continue;
                // A combined mesh can contain metal plus emissive material slots. Only tint energy.
                if (channel == Channel.Energy && materials.Length > 1 && !IsEnergyMaterial(material)) continue;
                var original = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(original, slot);
                result.Add(new Binding
                {
                    Renderer = renderer,
                    MaterialSlot = slot,
                    Channel = channel,
                    SequenceIndex = index,
                    SequenceCount = count,
                    Gain = material.name.IndexOf("CyanDim", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.45f : 1f,
                    Original = original,
                    Working = new MaterialPropertyBlock()
                });
            }
        }

        public static bool IsEnergyMaterial(Material material)
        {
            if (!material) return false;
            string name = material.name;
            return name.IndexOf("Cyan", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Amber", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Emission", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Fills missing references. Run on the closed-pose hierarchy before Play.</summary>
        [ContextMenu("Auto Bind Missing References")]
        public void AutoBindMissingReferences()
        {
            // Rebinding at runtime requires rebuilding snapshots and is intentionally rejected.
            if (initialized) return;
            Transform[] nodes = GetComponentsInChildren<Transform>(true);
            if (!coordinateRoot) coordinateRoot = transform;
            if (!gateLeafL) gateLeafL = FindNode(nodes, "GateLeaf_L");
            if (!gateLeafR) gateLeafR = FindNode(nodes, "GateLeaf_R");
            if (!portalSurface)
            {
                Transform node = FindNode(nodes, "Portal_Surface");
                if (node) portalSurface = node.GetComponentInChildren<Renderer>(true);
            }
            if (!fxOrbit) fxOrbit = FindNode(nodes, "FX_Orbit");
            FillGroup(ref orbitRenderers, fxOrbit);
            FillGroup(ref warningSegments, FindNode(nodes, "FX_WarningSegments"));
            FillGroup(ref prechargeRenderers, FindNode(nodes, "FX_Precharge"));

            if (colliderProxyRenderers == null || colliderProxyRenderers.Length == 0)
            {
                Renderer[] all = GetComponentsInChildren<Renderer>(true);
                var proxies = new List<Renderer>();
                for (int i = 0; i < all.Length; ++i)
                    if (IsColliderProxyName(all[i].name)) proxies.Add(all[i]);
                colliderProxyRenderers = proxies.ToArray();
            }

            if (energyRenderers == null || energyRenderers.Length == 0)
            {
                Renderer[] all = GetComponentsInChildren<Renderer>(true);
                var list = new List<Renderer>();
                for (int i = 0; i < all.Length; ++i)
                {
                    Renderer renderer = all[i];
                    if (IsColliderProxyName(renderer.name)) continue;
                    bool isEnergy = renderer.name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0;
                    Material[] materials = renderer.sharedMaterials;
                    for (int j = 0; j < materials.Length && !isEnergy; ++j)
                        isEnergy = IsEnergyMaterial(materials[j]);
                    if (isEnergy) list.Add(renderer);
                }
                energyRenderers = list.ToArray();
            }
        }

        private static void FillGroup(ref Renderer[] renderers, Transform root)
        {
            if (renderers != null && renderers.Length > 0) return;
            renderers = root ? root.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            // Name sequential meshes with zero-padded indices, e.g. Warning_00 ... Warning_15.
            Array.Sort(renderers, CompareRenderers);
        }

        public static bool IsColliderProxyName(string name)
        {
            return name.StartsWith("COL_", StringComparison.Ordinal)
                || name.StartsWith("UCX_", StringComparison.Ordinal);
        }

        private static int CompareRenderers(Renderer left, Renderer right)
        {
            return string.Compare(left ? left.name : string.Empty,
                right ? right.name : string.Empty, StringComparison.Ordinal);
        }

        private static Transform FindNode(Transform[] nodes, string expected)
        {
            for (int i = 0; i < nodes.Length; ++i)
                if (string.Equals(nodes[i].name, expected, StringComparison.Ordinal)) return nodes[i];
            return null;
        }

        private void OnValidate()
        {
            transitionSeconds = Mathf.Max(0f, transitionSeconds);
            openTravel = Mathf.Max(0f, openTravel);
            standbyEmission = Mathf.Max(0f, standbyEmission);
            openEmission = Mathf.Max(0f, openEmission);
            warningEmission = Mathf.Max(0f, warningEmission);
        }

        private void OnDestroy()
        {
            if (!initialized) return;
            // Restore the exact material overrides captured at initialization. Shared assets were
            // never modified. The five owned properties must not have a competing controller.
            for (int i = 0; i < bindings.Length; ++i)
            {
                Binding binding = bindings[i];
                if (binding.Renderer)
                    binding.Renderer.SetPropertyBlock(binding.Original.isEmpty ? null : binding.Original,
                        binding.MaterialSlot);
            }
            for (int i = 0; i < rendererSnapshots.Length; ++i)
                SetRendererEnabled(rendererSnapshots[i].Renderer, rendererSnapshots[i].Enabled);
            if (driveDoorTransforms)
            {
                if (gateLeafL) gateLeafL.localPosition = leafClosedL;
                if (gateLeafR) gateLeafR.localPosition = leafClosedR;
            }
            if (driveOrbitTransform && fxOrbit) fxOrbit.localRotation = orbitRestRotation;
        }
    }
}
