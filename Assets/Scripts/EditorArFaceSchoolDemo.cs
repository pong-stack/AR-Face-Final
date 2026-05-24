using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Windows Editor: webcam backdrop + stand-in mesh + props. Optional skin-blob tracking from
/// <see cref="WebCamTexture"/> moves the demo root (approx centroid, bbox-scale distance, ellipse roll + pseudo yaw/pitch).
/// Device AR face meshes outclass this; use there with <see cref="ARFaceManager"/> enabled instead.
/// </summary>
[DefaultExecutionOrder(-32000)]
public sealed class EditorArFaceSchoolDemo : MonoBehaviour
{
    const string PropsAttachChildName = "Props_Attach_Point";

    [SerializeField]
    Transform demoRoot;

    [SerializeField]
    GameObject arDefaultFacePrefab;

    [SerializeField]
    [Tooltip("Optional fallback when Filter Prefabs is empty. Prefer assigning Face_Reference_Mesh (or mask) under Filter Prefabs.")]
    GameObject propsCrazyEyesPrefab;

    [SerializeField]
    GameObject[] filterPrefabs;

    [SerializeField]
    [Tooltip("Creates a fullscreen overlay with a Next Filter button while in Editor Play mode.")]
    bool autoCreateFilterSwitchUi = true;

    [SerializeField]
    string nextFilterButtonLabel = "Next Filter";

    [SerializeField]
    Camera mainCameraOverride;

    [SerializeField]
    EditorVideoBackdrop videoBackdropSource;

    [Header("Stand-in placement (camera-forward baseline)")]
    [SerializeField]
    float editorPlaneAlongCameraForward = -1f;

    [SerializeField]
    float forwardBiasInFrontOfBackdrop = 0.08f;

    [SerializeField]
    Vector3 fallbackWorldPosition = new Vector3(0f, 0f, 8f);

    [SerializeField]
    Quaternion fallbackWorldRotation = Quaternion.identity;

    [Header("Face mesh scale (initial fit)")]
    [SerializeField]
    bool autoFitFaceMeshToViewport = true;

    [SerializeField]
    [Range(0.15f, 0.95f)]
    float viewportHeightFractionForStandInFace = 0.42f;

    [SerializeField]
    float faceStandInScaleMultiplier = 1f;

    [SerializeField]
    Vector3 manualFaceLocalScale = Vector3.one;

    [Header("Props")]
    [SerializeField]
    bool propsAttachToMainCamera;

    [SerializeField]
    Vector3 propsHudLocalPosition = new Vector3(0f, 0.06f, 0.42f);

    [SerializeField]
    Vector3 propsHudLocalEuler = new Vector3(0f, 180f, 0f);

    [SerializeField]
    float propsHudUniformScale = 1.4f;

    [Header("Glasses / eye alignment (webcam → plane)")]
    [SerializeField]
    [Tooltip("Skin centroid sits around cheeks; shift aim upward toward eye-line (multiplied by face bbox height in 0–1 image space).")]
    float eyeLineShiftUpBBoxFraction = 0.165f;

    [SerializeField]
    [Tooltip("Horizontal shift × bbox width (+ = right in unmirrored webcam image).")]
    float eyeLineShiftRightBBoxFraction = 0f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Blend centroid X toward bbox midpoint so glasses span both eyes symmetrically.")]
    float interpupillaryHorizontalBlend = 0.42f;

    [SerializeField]
    [Tooltip("Slides the rig slightly toward the camera along the anchor→camera ray (sit forward on skull).")]
    float eyeTowardCameraMeters = 0.028f;

    [Header("Editor webcam pose (skin blob)")]
    [SerializeField]
    bool useWebcamFacePoseTracking = true;

    [SerializeField]
    WebcamToRenderTexture webcamForPoseTracking;

    [SerializeField]
    int trackingProcessWidth = 240;

    [SerializeField]
    [Tooltip("Run GPU→CPU skin pass every N frames.")]
    int processEveryNFrames = 3;

    [SerializeField]
    float smoothResponseHz = 10f;

    [SerializeField]
    float lateralPlaneGain = 1f;

    [SerializeField]
    float verticalPlaneGain = 1f;

    [SerializeField]
    float roiMarginX = 0.08f;

    [SerializeField]
    float roiMarginY = 0.06f;

    [SerializeField]
    int minSkinPixels = 350;

    [SerializeField]
    bool horizontalMirrorSelfieStyle = true;

    [SerializeField]
    float yawMaxDegreesFromCenter = 40f;

    [SerializeField]
    float pitchMaxDegreesFromCenter = 32f;

    [SerializeField]
    [Range(0f, 2f)]
    float rollEllipseGain = 0.45f;

    [Header("Head rotation — smoothing & clamps")]
    [SerializeField]
    [Range(0f, 1.5f)]
    [Tooltip("<1 dampens centroid-based pitch/yaw so filters do not snap.")]
    float headPitchYawScale = 0.5f;

    [SerializeField]
    [Range(0f, 1.5f)]
    float headRollScale = 0.3f;

    [SerializeField]
    float appliedHeadYawClampDegrees = 20f;

    [SerializeField]
    float appliedHeadPitchClampDegrees = 16f;

    [SerializeField]
    float appliedHeadRollClampDegrees = 18f;

    [SerializeField]
    [Tooltip("Lower = smoother head tilt, higher latency.")]
    float headEulerSmoothHz = 5f;

    [SerializeField]
    [Tooltip("Extra low-pass after Euler (reduces jitter from skin blob ellipse).")]
    float headRotationSlerpHz = 10f;

    [SerializeField]
    float depthBBoxGain = 0.45f;

    [SerializeField]
    float maxDepthBBoxShift = 0.42f;

    [SerializeField]
    float planeDistanceSmoothHz = 12f;

    [SerializeField]
    float smoothScaleMultiplierHz = 8f;

    [SerializeField]
    [Range(0.01f, 0.35f)]
    float normHeightRefAdaptiveRate = 0.055f;

    [SerializeField]
    int lostFramesBeforeCenterOnly = 28;

    [Header("Estimator — segmentation & inertia")]
    [SerializeField]
    bool morphologyOpening = true;

    [SerializeField]
    [Range(0, 4)]
    int morphologyPasses = 1;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("Higher blends more inertia between heavy webcam samples (fewer centroid pops).")]
    float estimatorTemporalStrength = 0.48f;

    [SerializeField]
    [Range(0.6f, 1.95f)]
    [Tooltip("Higher makes yaw/pitch snap less near neutral (soft dead zone curve).")]
    float yawPitchCurveExponent = 0.93f;

    [Header("Stability — planar jitter suppression")]
    [SerializeField]
    [Tooltip("Ignore tiny lateral jitter as a fraction of world half-extent along camera plane.")]
    float lateralDeadZoneViewport = 0.014f;

    [SerializeField]
    [Tooltip("Resets planar dead-zone memory when tracking resets.")]
    bool resetLateralStickyOnTrackingLoss = true;

    [Header("Canonical anchors (built from AR face mesh local bounds at runtime, Editor-only)")]
    [SerializeField]
    float anchorHeadUpExtY = 0.9f;

    [SerializeField]
    float anchorHeadForwardExtZ = 0.09f;

    [SerializeField]
    float anchorEyesUpExtY = 0.42f;

    [SerializeField]
    float anchorEyesForwardExtZ = 0.92f;

    [SerializeField]
    float anchorNoseDownExtY = 0.05f;

    [SerializeField]
    float anchorNoseForwardExtZ = 1.02f;

    [SerializeField]
    float anchorMouthDownExtY = 0.38f;

    [SerializeField]
    float anchorMouthForwardExtZ = 0.9f;

    [Header("Full-face mask (Editor webcam)")]
    [SerializeField]
    [Tooltip(
        "When the filter prefab uses EditorFilterPlacement.attachTo = FaceSurface, it parents to the spawned AR face mesh so it follows position, rotation (head), and scale. Use (~0,0,0.02) to sit slightly in front of canonical face verts.")]
    Vector3 editorFullFaceMaskLocalPosition = new Vector3(0f, 0f, 0.02f);

    [SerializeField]
    [Tooltip("Usually Vector3.one; combined with viewport auto-fit on the AR stand-in mesh.")]
    Vector3 editorFullFaceMaskLocalScale = Vector3.one;

    float _resolvedPlaneAlongCameraForward = 8f;
    Transform _spawnedFace;
    Transform _propsAttachAnchor;
    Camera _setupCamera;
    GameObject _activeFilterInstance;
    int _filterIndex;
    GameObject _filterSwitchUiRoot;

    float _baselineFaceUniformScale = 1f;
    bool _setupComplete;

    readonly WebcamSkinFacePoseEstimator _poseEstimator = new WebcamSkinFacePoseEstimator();

    MaterialPropertyBlock _filterPropBlock;

    GameObject[] _cachedEffectiveFilters;

    WebcamSkinFacePoseEstimator.TemporalState _poseTemporal;

    Transform _anchorHead;
    Transform _anchorEyes;
    Transform _anchorNose;
    Transform _anchorMouth;

    Vector3 _lateralStickyDead;
    bool _hasLateralStickyDead;

    static readonly int ShaderMainTexId = Shader.PropertyToID("_MainTex");
    static readonly int ShaderColorId = Shader.PropertyToID("_Color");

    static Material CachedSpritesDefaultDrawing;
    static Material CachedUnlitColorDrawing;

    static readonly List<Material> ScratchSharedMaterials = new List<Material>(8);

    WebcamSkinFacePoseEstimator.Sample _lastSample;

    Vector3 _lateralSmooth;
    Vector3 _lateralVelSm;
    Vector3 _eulerSmooth;
    Vector3 _eulerVelSm;
    float _planeSmooth;
    float _planeVelSm;
    float _scaleMulSmooth = 1f;
    float _scaleMulVelSm;
    float _normBBoxHeightReference = -1f;
    int _lostPoseFrames;

    /// <summary>Runtime head anchor built from mesh bounds (may be null if mesh missing).</summary>
    public Transform AnchorHead => _anchorHead;

    /// <summary>Eye-line anchor for glasses / eye props.</summary>
    public Transform AnchorEyes => _anchorEyes;

    /// <summary>Nose-ring / nose overlays.</summary>
    public Transform AnchorNose => _anchorNose;

    /// <summary>Moustache / mouth filters.</summary>
    public Transform AnchorMouth => _anchorMouth;

    void Awake()
    {
        // MaterialPropertyBlock must not be created in field initializer (Unity disallows NativeObject ctor from MonoBehaviour ctor chain).
        if (_filterPropBlock == null)
            _filterPropBlock = new MaterialPropertyBlock();

        if (!Application.isEditor)
            return;

        EnsureEditorEventSystem();

        foreach (ARFaceManager m in FindObjectsByType<ARFaceManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            m.enabled = false;
        foreach (ARCameraBackground b in FindObjectsByType<ARCameraBackground>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            b.enabled = false;
        foreach (ARSession s in FindObjectsByType<ARSession>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            s.enabled = false;

        if (demoRoot == null)
            demoRoot = new GameObject("Editor Face Demo Root").transform;

        _lastSample = default;
    }

    void OnDestroy()
    {
        _poseEstimator.Release();
        if (_filterSwitchUiRoot != null)
            Destroy(_filterSwitchUiRoot);
    }

    void Start()
    {
        if (!Application.isEditor)
            return;
        StartCoroutine(EditorDemoSetup());
    }

    void LateUpdate()
    {
        if (!Application.isEditor || !_setupComplete || demoRoot == null)
            return;

        Camera cam = ActiveCamera();
        if (cam == null)
            return;

        WebcamToRenderTexture binder = webcamForPoseTracking != null ? webcamForPoseTracking : ResolveWebCamSource();

        bool canTrack =
            useWebcamFacePoseTracking &&
            !propsAttachToMainCamera &&
            _spawnedFace != null &&
            binder != null &&
            binder.ActiveWebCam != null &&
            binder.ActiveWebCam.isPlaying &&
            binder.ActiveWebCam.width > 16;

        if (!canTrack)
        {
            AlignBillboard(cam);
            return;
        }

        WebCamTexture wc = binder.ActiveWebCam;
        bool doHeavy = Time.frameCount % Mathf.Max(processEveryNFrames, 1) == 0;

        if (doHeavy)
        {
            WebcamSkinFacePoseEstimator.Sample measured;
            bool ok =
                _poseEstimator.TryEstimate(
                    wc,
                    trackingProcessWidth,
                    horizontalMirrorSelfieStyle,
                    minSkinPixels,
                    roiMarginX,
                    roiMarginY,
                    yawMaxDegreesFromCenter,
                    pitchMaxDegreesFromCenter,
                    rollEllipseGain,
                    morphologyOpening,
                    morphologyPasses,
                    ref _poseTemporal,
                    estimatorTemporalStrength,
                    yawPitchCurveExponent,
                    out measured) && measured.Valid;
            if (ok)
            {
                _lastSample = measured;
                _lostPoseFrames = 0;
            }
            else if (_lastSample.Valid)
                _lostPoseFrames++;
        }

        if (_lostPoseFrames > lostFramesBeforeCenterOnly)
        {
            _normBBoxHeightReference = -1f;
            _lastSample = default;
            _poseTemporal = default;
            _lostPoseFrames = 0;
            if (resetLateralStickyOnTrackingLoss)
                _hasLateralStickyDead = false;

            AlignBillboard(cam);
            return;
        }

        if (!_lastSample.Valid)
        {
            AlignBillboard(cam);
            return;
        }

        WebcamSkinFacePoseEstimator.Sample src = _lastSample;

        float capH = Mathf.Max(src.BboxNormH, 0.034f);
        float capW = Mathf.Max(src.BboxNormW, 0.034f);

        // Horizontal: centroid often drifts sideways; blend toward bbox midpoint for both eyes.
        float cxBlend = Mathf.Lerp(src.CenterXN, src.BboxCenterXN, interpupillaryHorizontalBlend);
        cxBlend = Mathf.Clamp01(cxBlend + eyeLineShiftRightBBoxFraction * capW);

        // Vertical: eyes sit above skin centroid → shift aim upward in normalized image space.
        float cyEye = Mathf.Clamp01(src.CenterYN - eyeLineShiftUpBBoxFraction * capH);

        float nearPlane = cam.nearClipPlane + 0.06f;
        float farPlane = Mathf.Max(cam.farClipPlane - 0.1f, nearPlane + 0.25f);

        if (_normBBoxHeightReference < 0f)
            _normBBoxHeightReference = src.BboxNormH;
        _normBBoxHeightReference = Mathf.Lerp(_normBBoxHeightReference, Mathf.Clamp01(src.BboxNormH), normHeightRefAdaptiveRate);

        float depthShift = Mathf.Clamp(
            (_normBBoxHeightReference - src.BboxNormH) * depthBBoxGain,
            -maxDepthBBoxShift, maxDepthBBoxShift);

        float planeTarget = Mathf.Clamp(_resolvedPlaneAlongCameraForward + depthShift, nearPlane, farPlane);

        float cxM = horizontalMirrorSelfieStyle ? (1f - cxBlend) : cxBlend;
        float nx = (cxM - 0.5f) * 2f * lateralPlaneGain;
        float ny = -(cyEye - 0.5f) * 2f * verticalPlaneGain;

        float halfVH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * planeTarget;
        float halfHW = halfVH * cam.aspect;

        Vector3 lateralTarget =
            cam.transform.right * (nx * halfHW) +
            cam.transform.up * (ny * halfVH);

        if (lateralDeadZoneViewport > 1e-7f && lateralPlaneGain > 1e-5f && verticalPlaneGain > 1e-5f)
        {
            float thr = Mathf.Max(halfHW, halfVH) * lateralDeadZoneViewport;
            float thrSq = thr * thr;
            if (!_hasLateralStickyDead)
            {
                _lateralStickyDead = lateralTarget;
                _hasLateralStickyDead = true;
            }
            else if ((lateralTarget - _lateralStickyDead).sqrMagnitude <= thrSq)
                lateralTarget = _lateralStickyDead;
            else
                _lateralStickyDead = lateralTarget;
        }

        float confCurve = Mathf.Clamp01(Mathf.SmoothStep(0.06f, 0.93f, src.Confidence));

        float axisScale = Mathf.Lerp(0.74f * confCurve + 0.14f, 1f, confCurve);

        Vector3 eulerRaw = new Vector3(
            src.PitchDeg * headPitchYawScale * axisScale,
            src.YawDeg * headPitchYawScale * axisScale,
            src.RollDeg * headRollScale * Mathf.Lerp(0.73f + 0.06f * confCurve, 1f, confCurve));

        eulerRaw.x = Mathf.Clamp(eulerRaw.x, -appliedHeadPitchClampDegrees, appliedHeadPitchClampDegrees);
        eulerRaw.y = Mathf.Clamp(eulerRaw.y, -appliedHeadYawClampDegrees, appliedHeadYawClampDegrees);
        eulerRaw.z = Mathf.Clamp(eulerRaw.z, -appliedHeadRollClampDegrees, appliedHeadRollClampDegrees);

        float refH = Mathf.Max(_normBBoxHeightReference, 0.05f);
        float scaleTarget = Mathf.Clamp(refH / Mathf.Max(src.BboxNormH, 0.038f), 0.52f, 2.58f);

        float confSqrt = Mathf.Sqrt(confCurve);

        float latSmoothSec =
            Mathf.Max(
                0.022f,
                1f / Mathf.Max(
                    Mathf.Lerp(smoothResponseHz * 0.66f, smoothResponseHz + 38f * confSqrt, Mathf.Clamp01(src.Confidence)),
                    4f));
        float eulerSmoothSec = Mathf.Max(0.04f, 1f / Mathf.Max(headEulerSmoothHz, 0.5f));

        _lateralSmooth = Vector3.SmoothDamp(_lateralSmooth, lateralTarget, ref _lateralVelSm, latSmoothSec);
        _eulerSmooth = Vector3.SmoothDamp(_eulerSmooth, eulerRaw, ref _eulerVelSm, eulerSmoothSec);

        _planeSmooth = Mathf.SmoothDamp(_planeSmooth, planeTarget, ref _planeVelSm,
            Mathf.Max(0.02f, 1f / Mathf.Max(planeDistanceSmoothHz, 4f)));

        _scaleMulSmooth = Mathf.SmoothDamp(_scaleMulSmooth, scaleTarget, ref _scaleMulVelSm,
            Mathf.Max(0.02f, 1f / Mathf.Max(smoothScaleMultiplierHz, 2f)));

        Vector3 anchor =
            cam.transform.position +
            cam.transform.forward * _planeSmooth +
            _lateralSmooth;

        if (eyeTowardCameraMeters > 1e-5f)
        {
            Vector3 toCam = cam.transform.position - anchor;
            float m = toCam.magnitude;
            if (m > 1e-4f)
                anchor += (toCam / m) * eyeTowardCameraMeters;
        }

        Quaternion billboard = Quaternion.LookRotation(cam.transform.position - anchor, cam.transform.up);
        Quaternion oriented = billboard * Quaternion.Euler(_eulerSmooth.x, _eulerSmooth.y, _eulerSmooth.z);

        float rotT = 1f - Mathf.Exp(-Mathf.Max(0.01f, headRotationSlerpHz) * Mathf.Min(Time.deltaTime, 0.05f));
        demoRoot.SetPositionAndRotation(anchor, Quaternion.Slerp(demoRoot.rotation, oriented, Mathf.Clamp01(rotT)));

        demoRoot.localScale = Vector3.one;

        _spawnedFace.localScale = Vector3.one * (_baselineFaceUniformScale * _scaleMulSmooth);
    }

    void AlignBillboard(Camera cam)
    {
        PlaneAlignDemoRoot(cam, _resolvedPlaneAlongCameraForward);
        _planeSmooth = _resolvedPlaneAlongCameraForward;

        _hasLateralStickyDead = false;
        _poseTemporal = default;

        _lateralSmooth = Vector3.SmoothDamp(_lateralSmooth, Vector3.zero, ref _lateralVelSm, 0.1f);
        _eulerSmooth = Vector3.SmoothDamp(_eulerSmooth, Vector3.zero, ref _eulerVelSm, 0.15f);
        _scaleMulSmooth = Mathf.SmoothDamp(_scaleMulSmooth, 1f, ref _scaleMulVelSm, 0.12f);

        if (_spawnedFace != null)
            _spawnedFace.localScale = Vector3.one * (_baselineFaceUniformScale * _scaleMulSmooth);
    }

    Camera ActiveCamera() => mainCameraOverride != null ? mainCameraOverride : Camera.main;

    IEnumerator EditorDemoSetup()
    {
        Transform root = demoRoot;
        if (root == null)
            yield break;

        yield return null;

        Camera cam = ActiveCamera();

        EditorVideoBackdrop backdrop = videoBackdropSource;
        if (backdrop == null)
        {
            EditorVideoBackdrop[] backs = FindObjectsByType<EditorVideoBackdrop>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (backs != null && backs.Length > 0)
                backdrop = backs[0];
        }

        float planeDist = ResolvePlaneAlongCamera(cam, backdrop);
        _resolvedPlaneAlongCameraForward = planeDist;
        _planeSmooth = planeDist;

        if (cam != null)
            PlaneAlignDemoRoot(cam, planeDist);
        else
            root.SetPositionAndRotation(fallbackWorldPosition, fallbackWorldRotation);

        if (arDefaultFacePrefab == null)
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Assign AR Default Face prefab.", this);
            yield break;
        }

        _cachedEffectiveFilters = EffectiveFilterPrefabs();
        if (_cachedEffectiveFilters.Length == 0)
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Add a prefab (e.g. Face_Reference_Mesh) to Filter Prefabs.", this);
            yield break;
        }

        for (var i = root.childCount - 1; i >= 0; i--)
            Destroy(root.GetChild(i).gameObject);

        yield return null;

        GameObject face = Instantiate(arDefaultFacePrefab, root);
        face.name = "AR Default Face";
        face.transform.localPosition = Vector3.zero;
        face.transform.localRotation = Quaternion.identity;
        face.transform.localScale = Vector3.one;

        bool useAutoFit = cam != null && autoFitFaceMeshToViewport;
        if (!useAutoFit)
            face.transform.localScale = manualFaceLocalScale;

        DisableArFoundationBehaviours(face);

        if (useAutoFit)
        {
            PlaneAlignDemoRoot(cam, planeDist);
            ApplyFaceStandInViewportScale(cam, face, planeDist);
        }

        _spawnedFace = face.transform;
        _baselineFaceUniformScale = _spawnedFace.localScale.x;
        _setupCamera = cam;

        Transform propsAttach = face.transform.Find(PropsAttachChildName);
        if (propsAttach == null)
            propsAttach = face.transform;
        _propsAttachAnchor = propsAttach;

        BuildRuntimeFaceAnchors(face);

        _filterIndex = 0;
        SpawnActiveFilterPrefab(_cachedEffectiveFilters);

        if (autoCreateFilterSwitchUi && _cachedEffectiveFilters.Length > 1)
            BuildEditorFilterSwitcherUi();

        _setupComplete = true;
    }

    /// <summary>Editor/UI: advances to next filter prefab.</summary>
    public void CycleToNextFilter()
    {
        if (!Application.isEditor || !_setupComplete)
            return;

        EnsureEffectiveFilterCache();
        if (_cachedEffectiveFilters.Length <= 1)
            return;

        _filterIndex = (_filterIndex + 1) % _cachedEffectiveFilters.Length;

        SpawnActiveFilterPrefab(_cachedEffectiveFilters);
    }

    GameObject[] EffectiveFilterPrefabs()
    {
        if (filterPrefabs != null && filterPrefabs.Length > 0)
        {
            var accepted = new List<GameObject>(filterPrefabs.Length);
            foreach (GameObject go in filterPrefabs)
            {
                if (go == null)
                {
                    Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Filter prefabs list contains a null entry; skipping.", this);
                    continue;
                }

                if (!PrefabHierarchyHasRenderableMeshFilter(go))
                {
                    Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Filter prefab '{go.name}' has no usable MeshRenderer or SkinnedMeshRenderer; skipping.", this);
                    continue;
                }

                accepted.Add(go);
            }

            if (accepted.Count == 0)
                return propsCrazyEyesPrefab != null && PrefabHierarchyHasRenderableMeshFilter(propsCrazyEyesPrefab)
                    ? new[] { propsCrazyEyesPrefab }
                    : Array.Empty<GameObject>();

            return accepted.ToArray();
        }

        return propsCrazyEyesPrefab != null && PrefabHierarchyHasRenderableMeshFilter(propsCrazyEyesPrefab)
            ? new[] { propsCrazyEyesPrefab }
            : Array.Empty<GameObject>();
    }

    static bool PrefabHierarchyHasRenderableMeshFilter(GameObject go)
    {
        if (go == null)
            return false;

        Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return false;

        foreach (Renderer r in renderers)
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
                return true;
        }

        return false;
    }

#if UNITY_EDITOR
    static void RemoveMissingBehaviourScriptsRecursive(GameObject root)
    {
        if (root == null)
            return;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            UnityEditor.GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
    }
#else
    static void RemoveMissingBehaviourScriptsRecursive(GameObject root)
    {
    }
#endif

    void EnsureEffectiveFilterCache()
    {
        if (_cachedEffectiveFilters != null && _cachedEffectiveFilters.Length > 0)
            return;

        _cachedEffectiveFilters = EffectiveFilterPrefabs();
    }

    void SpawnActiveFilterPrefab(GameObject[] list)
    {
        if (_activeFilterInstance != null)
        {
            Destroy(_activeFilterInstance);
            _activeFilterInstance = null;
        }

        if (list == null || list.Length == 0)
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Filter prefab list is null or empty.", this);
            return;
        }

        if (_propsAttachAnchor == null)
            return;

        GameObject pref = list[Mathf.Clamp(_filterIndex, 0, list.Length - 1)];
        if (pref == null)
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Filter prefab is null.", this);
            return;
        }

        if (!PrefabHierarchyHasRenderableMeshFilter(pref))
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Filter prefab '{pref.name}' has no usable MeshRenderer or SkinnedMeshRenderer.", this);
            return;
        }

        GameObject spawned = Instantiate(pref);
        RemoveMissingBehaviourScriptsRecursive(spawned);

        if (!PrefabHierarchyHasRenderableMeshFilter(spawned))
        {
            Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Instantiated filter '{spawned.name}' lost all mesh renderers (check hierarchy). Destroying spawn.", spawned);
            Destroy(spawned);
            return;
        }

        spawned.name = pref.name + " (clone)";
        _activeFilterInstance = spawned;

        if (propsAttachToMainCamera && _setupCamera != null)
        {
            spawned.transform.SetParent(_setupCamera.transform, false);
            spawned.transform.localPosition = propsHudLocalPosition;
            spawned.transform.localRotation = Quaternion.Euler(propsHudLocalEuler);
            spawned.transform.localScale = Vector3.one * propsHudUniformScale;
        }
        else
        {
            EditorFilterPlacement placement = spawned.GetComponentInChildren<EditorFilterPlacement>(true)
                ?? spawned.GetComponent<EditorFilterPlacement>();
            Transform attach = ResolveEditorAttach(placement);

            if (attach == null)
            {
                Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: No attach transform for spawned filter '{spawned.name}'.", this);
                Destroy(spawned);
                _activeFilterInstance = null;
                return;
            }

            spawned.transform.SetParent(attach, false);

            bool faceSurface =
                placement != null && placement.AttachTo == EditorFaceFilterAttach.FaceSurface;

            if (faceSurface)
            {
                spawned.transform.localPosition = editorFullFaceMaskLocalPosition;
                spawned.transform.localRotation = Quaternion.identity;
                spawned.transform.localScale = editorFullFaceMaskLocalScale;
            }
            else
            {
                spawned.transform.localPosition = Vector3.zero;
                spawned.transform.localRotation = Quaternion.identity;
                spawned.transform.localScale = Vector3.one;
            }
        }

        ConfigureFiltersDrawingRuntime(spawned);
        ForcePropsVisible(spawned);

        foreach (Animator animator in spawned.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
    }

    Transform ResolveEditorAttach(EditorFilterPlacement placement)
    {
        if (_propsAttachAnchor == null)
            return demoRoot != null ? demoRoot : transform;

        EditorFaceFilterAttach mode =
            placement == null ? EditorFaceFilterAttach.PropsFallback : placement.AttachTo;

        switch (mode)
        {
            case EditorFaceFilterAttach.Head:
                return _anchorHead != null ? _anchorHead : _propsAttachAnchor;
            case EditorFaceFilterAttach.Eyes:
                return _anchorEyes != null ? _anchorEyes : _propsAttachAnchor;
            case EditorFaceFilterAttach.Nose:
                return _anchorNose != null ? _anchorNose : _propsAttachAnchor;
            case EditorFaceFilterAttach.Mouth:
                return _anchorMouth != null ? _anchorMouth : _propsAttachAnchor;
            case EditorFaceFilterAttach.FaceSurface:
                return _spawnedFace != null ? _spawnedFace : _propsAttachAnchor;
            default:
                return _propsAttachAnchor;
        }
    }

    void DestroyRuntimeEditorAnchors(Transform root)
    {
        if (root == null)
            return;

        const string Prefix = "EditorAnchor_";

        for (var i = root.childCount - 1; i >= 0; i--)
        {
            Transform ch = root.GetChild(i);

            string n = ch.name;
            if (n.StartsWith(Prefix, StringComparison.Ordinal))
                Destroy(ch.gameObject);
        }
    }

    void BuildRuntimeFaceAnchors(GameObject face)
    {
        if (face == null)
            return;

        DestroyRuntimeEditorAnchors(face.transform);

        Quaternion alignWithPropsBaseline = RuntimePropsBaselineRotation(face);

        var mf = face.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
        {
            _anchorHead = _anchorEyes = _anchorNose = _anchorMouth = null;
            return;
        }

        Bounds lb = mf.sharedMesh.bounds;
        Vector3 c = lb.center;
        Vector3 ext = lb.extents;

        Transform parent = face.transform;

        _anchorHead = CreateRuntimeAnchor(parent, $"EditorAnchor_{nameof(EditorFaceFilterAttach.Head)}",
            new Vector3(c.x, c.y + anchorHeadUpExtY * ext.y, c.z + anchorHeadForwardExtZ * ext.z), alignWithPropsBaseline);
        _anchorEyes = CreateRuntimeAnchor(parent, $"EditorAnchor_{nameof(EditorFaceFilterAttach.Eyes)}",
            new Vector3(c.x, c.y + anchorEyesUpExtY * ext.y, c.z + anchorEyesForwardExtZ * ext.z), alignWithPropsBaseline);
        _anchorNose = CreateRuntimeAnchor(parent, $"EditorAnchor_{nameof(EditorFaceFilterAttach.Nose)}",
            new Vector3(c.x, c.y - anchorNoseDownExtY * ext.y, c.z + anchorNoseForwardExtZ * ext.z), alignWithPropsBaseline);
        _anchorMouth = CreateRuntimeAnchor(parent, $"EditorAnchor_{nameof(EditorFaceFilterAttach.Mouth)}",
            new Vector3(c.x, c.y - anchorMouthDownExtY * ext.y, c.z + anchorMouthForwardExtZ * ext.z), alignWithPropsBaseline);
    }

    static Quaternion RuntimePropsBaselineRotation(GameObject face)
    {
        if (face == null)
            return Quaternion.identity;

        Transform props = face.transform.Find(PropsAttachChildName);
        return props != null ? props.localRotation : Quaternion.identity;
    }

    Transform CreateRuntimeAnchor(Transform parent, string anchorName, Vector3 localPosition, Quaternion localRotation)
    {
        var node = new GameObject(anchorName);
        node.transform.SetParent(parent, false);
        node.transform.localPosition = localPosition;
        node.transform.localRotation = localRotation;
        node.transform.localScale = Vector3.one;
        node.layer = parent.gameObject.layer;
        return node.transform;
    }

    void ConfigureFiltersDrawingRuntime(GameObject root)
    {
        if (root == null || _filterPropBlock == null)
            return;

        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
            return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            if (renderer is not MeshRenderer && renderer is not SkinnedMeshRenderer)
                continue;

            ScratchSharedMaterials.Clear();
            renderer.GetSharedMaterials(ScratchSharedMaterials);

            if (ScratchSharedMaterials.Count == 0)
                continue;

            Material srcMat = ScratchSharedMaterials[0];
            if (srcMat == null)
                continue;

            // Keep Built-in shaders that already render correctly (e.g. ARFaceFilter/FaceMaskTransparent with keyed alpha).
            if (!RendererMaterialNeedsBuiltinEditorRemap(srcMat))
            {
                renderer.SetPropertyBlock(null);
                continue;
            }

            Texture albedo = null;
            if (srcMat.HasProperty("_BaseMap"))
                albedo = srcMat.GetTexture("_BaseMap");
            if (albedo == null && srcMat.HasProperty("_MainTex"))
                albedo = srcMat.GetTexture("_MainTex");
            if (albedo == null)
                albedo = srcMat.mainTexture;

            Color tint = Color.white;
            bool hasBaseColorProp = srcMat.HasProperty("_BaseColor");
            if (hasBaseColorProp)
                tint = srcMat.GetColor("_BaseColor");
            if (srcMat.HasProperty("_Color"))
            {
                Color fromColorProp = srcMat.GetColor("_Color");
                // Many imported meshes tint only via _Color with _BaseColor left white — avoid flat white overlays.
                if (!hasBaseColorProp || TintLooksUnstainedNeutral(tint))
                    tint = fromColorProp;
            }

            if (tint.a < 0.01f && albedo == null)
                tint = new Color(1f, 0.35f, 0.85f, 1f);

            if (ScratchSharedMaterials.Count > 1)
            {
                Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: Renderer '{renderer.name}' uses multiple materials; remapping slot 0 for editor draw.", renderer);
            }

            Material sharedChosen = albedo != null ? EnsureCachedSpritesDrawing() : EnsureCachedUnlitColorDrawing();

            if (sharedChosen == null)
            {
                Debug.LogWarning($"{nameof(EditorArFaceSchoolDemo)}: No suitable Built-in RP shader found for '{renderer.name}'. Skipping draw remap.", renderer);
                continue;
            }

            _filterPropBlock.Clear();

            renderer.sharedMaterial = sharedChosen;

            if (albedo != null)
            {
                _filterPropBlock.SetTexture(ShaderMainTexId, albedo);
                _filterPropBlock.SetColor(ShaderColorId, tint);
            }
            else
                _filterPropBlock.SetColor(ShaderColorId, tint);

            renderer.SetPropertyBlock(_filterPropBlock);
        }
    }

    static bool TintLooksUnstainedNeutral(Color tint)
    {
        const float hi = 0.97f;
        return tint.r >= hi && tint.g >= hi && tint.b >= hi;
    }

    static bool RendererMaterialNeedsBuiltinEditorRemap(Material srcMat)
    {
        Shader s = srcMat != null ? srcMat.shader : null;
        if (s == null)
            return false;

        if (!s.isSupported)
            return true;

        string n = s.name;
        if (n.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
            return true;
        if (n.StartsWith("HDRP/", StringComparison.Ordinal))
            return true;

        return false;
    }

    static Material EnsureCachedSpritesDrawing()
    {
        if (CachedSpritesDefaultDrawing != null)
            return CachedSpritesDefaultDrawing;

        Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Texture");
        if (shader == null)
            return null;

        CachedSpritesDefaultDrawing = new Material(shader) { hideFlags = HideFlags.DontSave };
        return CachedSpritesDefaultDrawing;
    }

    static Material EnsureCachedUnlitColorDrawing()
    {
        if (CachedUnlitColorDrawing != null)
            return CachedUnlitColorDrawing;

        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
            return null;

        CachedUnlitColorDrawing = new Material(shader) { hideFlags = HideFlags.DontSave };
        return CachedUnlitColorDrawing;
    }

    static void EnsureEditorEventSystem()
    {
        EventSystem[] existing = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (existing != null && existing.Length > 0)
            return;

        GameObject esGo = new GameObject("Editor EventSystem");
        esGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<StandaloneInputModule>();
    }

    void BuildEditorFilterSwitcherUi()
    {
        if (!Application.isEditor || _filterSwitchUiRoot != null)
            return;

        GameObject canvasGo = new GameObject("Editor Face Filter Switcher UI");
        canvasGo.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
        _filterSwitchUiRoot = canvasGo;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;

        CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        GameObject btnGo = new GameObject("Next Filter Button", typeof(RectTransform));
        btnGo.transform.SetParent(canvasGo.transform, false);

        RectTransform rt = btnGo.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 0f);
        rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(240f, 60f);
        rt.anchoredPosition = new Vector2(-28f, 28f);

        Image img = btnGo.AddComponent<Image>();
        img.color = new Color(0.22f, 0.53f, 0.96f, 0.93f);

        Button btn = btnGo.AddComponent<Button>();
        ColorBlock cb = btn.colors;
        cb.highlightedColor = new Color(0.42f, 0.72f, 1f, 1f);
        cb.pressedColor = new Color(0.15f, 0.4f, 0.82f, 1f);
        btn.colors = cb;
        btn.onClick.AddListener(CycleToNextFilter);

        GameObject textGo = new GameObject("Label", typeof(RectTransform));
        textGo.transform.SetParent(btnGo.transform, false);

        RectTransform trt = textGo.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = Vector2.zero;
        trt.offsetMax = Vector2.zero;

        Text label = textGo.AddComponent<Text>();

        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null)
        {
            try
            {
                f = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial", "Helvetica" }, 22);
            }
            catch (Exception)
            {
                /* font optional */
            }
        }

        label.font = f;
        label.text = nextFilterButtonLabel;
        label.alignment = TextAnchor.MiddleCenter;
        label.resizeTextForBestFit = false;
        label.fontSize = 22;
        label.color = Color.white;
        label.raycastTarget = false;
    }

    float ResolvePlaneAlongCamera(Camera cam, EditorVideoBackdrop backdrop)
    {
        float baseBackdrop = backdrop != null ? backdrop.PlaneDistanceAlongCameraForward : 8f;
        float plane = editorPlaneAlongCameraForward >= 0f
            ? editorPlaneAlongCameraForward
            : Mathf.Max(
                cam != null ? cam.nearClipPlane + 0.05f : 0.15f,
                baseBackdrop - forwardBiasInFrontOfBackdrop);
        float maxZ = cam != null ? Mathf.Max(cam.farClipPlane - 0.1f, cam.nearClipPlane + 0.2f) : plane;
        return Mathf.Clamp(plane, cam != null ? cam.nearClipPlane + 0.05f : 0.15f, maxZ);
    }

    void PlaneAlignDemoRoot(Camera cam, float planeAlongCameraForward)
    {
        if (cam == null || demoRoot == null)
            return;

        Vector3 pos = cam.transform.position + cam.transform.forward * planeAlongCameraForward;
        Quaternion rot = Quaternion.LookRotation(cam.transform.position - pos, cam.transform.up);
        demoRoot.SetPositionAndRotation(pos, rot);
        demoRoot.localScale = Vector3.one;
    }

    void ApplyFaceStandInViewportScale(Camera cam, GameObject face, float planeDistance)
    {
        var mf = face.GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return;

        float planeDist = Mathf.Max(planeDistance, cam.nearClipPlane + 0.02f);
        float viewportHeight = 2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * planeDist;
        float targetWorldHeight = viewportHeight * viewportHeightFractionForStandInFace;
        Bounds lb = mf.sharedMesh.bounds;
        float meshHeight = Mathf.Max(lb.size.y, 1e-5f);
        float uniform = targetWorldHeight / meshHeight * faceStandInScaleMultiplier;
        face.transform.localScale = Vector3.one * uniform;
    }

    WebcamToRenderTexture ResolveWebCamSource()
    {
        WebcamToRenderTexture[] all = FindObjectsByType<WebcamToRenderTexture>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        return all != null && all.Length > 0 ? all[0] : null;
    }

    static void ForcePropsVisible(GameObject propsRoot)
    {
        foreach (Transform t in propsRoot.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = 0;
            t.gameObject.hideFlags = HideFlags.None;
        }

        foreach (Renderer renderer in propsRoot.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = true;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }

    static void DisableArFoundationBehaviours(GameObject root)
    {
        foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null)
                continue;
            string ns = mb.GetType().Namespace;
            if (ns != null && ns.StartsWith("UnityEngine.XR.ARFoundation"))
                mb.enabled = false;
        }
    }
}
