using System.Collections.Generic;
using UnityVolumeRendering;
using UnityEngine;

namespace VolumeRenderingSample
{
    /// <summary>
    /// 运行时 IMGUI 控制面板：数据集加载与切换、渲染模式（DVR/MIP/等值面）、切面平面、裁剪盒、采样率、传递函数重置。
    /// </summary>
    public class VolumeViewerUI : MonoBehaviour
    {
        private RuntimeVolumeLoader loader;
        private readonly Dictionary<VolumeRenderedObject, SlicingPlane> slicingPlanes = new Dictionary<VolumeRenderedObject, SlicingPlane>();

        private Rect panelRect = new Rect(10f, 10f, 300f, 10f);
        private float samplingRate = 1f;
        private SlicePlaneBinder boundSliceBinder;
        private float nextBinderScanTime;

        // ---- 切片查看器（屏幕 UI 直显，不依赖场景 Plane）----
        private Rect sliceWindowRect = new Rect(320f, 10f, 460f, 10f);
        private RenderTexture axialRT;    // 横向切片（沿容器 Z 轴）
        private RenderTexture coronalRT;  // 纵向切片（沿容器 Y 轴）
        private Material sliceUIMat;
        private float slicePosZ = 0.5f;
        private float slicePosY = 0.5f;
        private bool sliceViewerBroken;

        // ---- 分割叠加（第二阶段）----
        private Vector2 segScrollPos;

        // ---- Slicer vp.json 预设 ----
        private Vector2 presetScrollPos;
        private int selectedPresetIndex = -1;
        private float presetHuShift = 0f;

        private void Awake()
        {
            loader = GetComponent<RuntimeVolumeLoader>();
        }

        /// <summary>定期扫描场景中的 SlicePlaneBinder（编辑模式预先挂载或运行时绑定的都能被发现）。</summary>
        private void Update()
        {
            if (Time.unscaledTime >= nextBinderScanTime)
            {
                nextBinderScanTime = Time.unscaledTime + 1f;
                if (boundSliceBinder == null)
                {
#if UNITY_2023_1_OR_NEWER
                    boundSliceBinder = UnityEngine.Object.FindFirstObjectByType<SlicePlaneBinder>();
#else
                    boundSliceBinder = UnityEngine.Object.FindObjectOfType<SlicePlaneBinder>();
#endif
                }
            }

            RenderSliceTextures();
        }

        private void OnGUI()
        {
            panelRect = GUILayout.Window(0, panelRect, DrawPanel, "Volume Viewer (体渲染控制)");
            sliceWindowRect = GUILayout.Window(1, sliceWindowRect, DrawSliceViewer, "切片查看器 (Slice Viewer)");
        }

        /// <summary>把两幅切片渲染到 RenderTexture，供 IMGUI 窗口显示。</summary>
        private void RenderSliceTextures()
        {
            VolumeRenderedObject active = loader != null ? loader.ActiveObject : null;
            if (active == null || active.dataset == null || sliceViewerBroken)
                return;

            if (axialRT == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/SliceUI");
                if (shader == null)
                {
                    Debug.LogError("[VolumeViewerUI] 未找到切片查看器着色器 Resources/Shaders/SliceUI.shader");
                    sliceViewerBroken = true;
                    return;
                }
                sliceUIMat = new Material(shader);
                axialRT = new RenderTexture(256, 256, 0);
                coronalRT = new RenderTexture(256, 256, 0);
            }

            sliceUIMat.SetTexture("_DataTex", active.dataset.GetDataTexture());
            sliceUIMat.SetTexture("_TFTex", active.transferFunction != null
                ? active.transferFunction.GetTexture()
                : Texture2D.whiteTexture);

            // 分割叠加：有分割数据时，切片上用结构色替换组织色（隐藏的结构 alpha=0 自动不显示）
            bool hasSeg = active.GetOverlayType() != UnityVolumeRendering.OverlayType.None
                && active.GetSecondaryTransferFunction() != null;
            if (hasSeg)
            {
                Texture segDataTex = active.meshRenderer.sharedMaterial.GetTexture("_SecondaryDataTex");
                hasSeg = segDataTex != null;
                if (hasSeg)
                {
                    sliceUIMat.SetTexture("_SecondaryDataTex", segDataTex);
                    sliceUIMat.SetTexture("_SecondaryTFTex", active.GetSecondaryTransferFunction().GetTexture());
                }
            }
            sliceUIMat.SetFloat("_UseSegmentation", hasSeg ? 1f : 0f);

            sliceUIMat.SetFloat("_Axis", 2f); // 横向：沿 Z 轴
            sliceUIMat.SetFloat("_SlicePos", slicePosZ);
            Graphics.Blit(Texture2D.whiteTexture, axialRT, sliceUIMat);

            sliceUIMat.SetFloat("_Axis", 1f); // 纵向：沿 Y 轴
            sliceUIMat.SetFloat("_SlicePos", slicePosY);
            Graphics.Blit(Texture2D.whiteTexture, coronalRT, sliceUIMat);
        }

        private void DrawSliceViewer(int windowId)
        {
            VolumeRenderedObject active = loader != null ? loader.ActiveObject : null;
            if (active == null || active.dataset == null)
            {
                GUILayout.Label("请先加载体数据（Lung DICOM 或 Chest CT）");
                GUI.DragWindow();
                return;
            }
            if (sliceViewerBroken || axialRT == null)
            {
                GUILayout.Label("切片查看器不可用（见 Console 报错）");
                GUI.DragWindow();
                return;
            }

            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(210f));
            GUILayout.Label("横向切片（沿 Z 轴）");
            Rect axialRect = GUILayoutUtility.GetRect(200f, 200f);
            GUI.DrawTexture(axialRect, axialRT, ScaleMode.ScaleToFit, false);
            slicePosZ = GUILayout.HorizontalSlider(slicePosZ, 0f, 1f);
            GUILayout.Label($"层位置 {slicePosZ:0.00}");
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(210f));
            GUILayout.Label("纵向切片（沿 Y 轴）");
            Rect coronalRect = GUILayoutUtility.GetRect(200f, 200f);
            GUI.DrawTexture(coronalRect, coronalRT, ScaleMode.ScaleToFit, false);
            slicePosY = GUILayout.HorizontalSlider(slicePosY, 0f, 1f);
            GUILayout.Label($"层位置 {slicePosY:0.00}");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUI.DragWindow();
        }

        private void OnDestroy()
        {
            if (axialRT != null) axialRT.Release();
            if (coronalRT != null) coronalRT.Release();
        }

        private void DrawPanel(int windowId)
        {
            if (loader == null)
                return;

            if (loader.IsBusy)
            {
                GUILayout.Label(loader.ProgressDescription);
                Rect barRect = GUILayoutUtility.GetRect(0f, 18f, GUILayout.ExpandWidth(true));
                GUI.Box(barRect, GUIContent.none);
                GUI.Box(new Rect(barRect.x, barRect.y, barRect.width * loader.Progress01, barRect.height), $"{loader.Progress01:P0}");
                GUI.DragWindow();
                return;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("加载 Lung DICOM"))
                loader.LoadLungDicomAsync();
            if (GUILayout.Button("加载 Chest CT"))
                loader.LoadChestCtAsync();
            GUILayout.EndHorizontal();

            bool hasObject = false;
            IReadOnlyList<VolumeRenderedObject> objects = loader.LoadedObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                VolumeRenderedObject obj = objects[i];
                if (obj == null)
                    continue;
                hasObject = true;
                bool selected = GUILayout.Toggle(loader.ActiveObject == obj, obj.name, GUI.skin.button);
                if (selected && loader.ActiveObject != obj)
                    loader.SetActiveObject(obj);
            }

            VolumeRenderedObject active = loader.ActiveObject;
            if (hasObject && active != null)
            {
                GUILayout.Space(6f);
                GUILayout.Label($"当前：{active.name}  ({active.dataset.dimX}×{active.dataset.dimY}×{active.dataset.dimZ})");

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("DVR"))
                    ApplyRenderMode(active, UnityVolumeRendering.RenderMode.DirectVolumeRendering);
                if (GUILayout.Button("MIP"))
                    ApplyRenderMode(active, UnityVolumeRendering.RenderMode.MaximumIntensityProjectipon);
                if (GUILayout.Button("等值面"))
                    ApplyRenderMode(active, UnityVolumeRendering.RenderMode.IsosurfaceRendering);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                bool sliceOn = false;
                if (slicingPlanes.TryGetValue(active, out SlicingPlane existing) && existing != null)
                    sliceOn = existing.gameObject.activeSelf;
                bool newSliceOn = GUILayout.Toggle(sliceOn, "切面平面", GUI.skin.button);
                if (newSliceOn != sliceOn)
                    ToggleSlicingPlane(active, newSliceOn);
                if (GUILayout.Button("添加裁剪盒"))
                    VolumeObjectFactory.SpawnCutoutBox(active);
                GUILayout.EndHorizontal();

                if (GUILayout.Button("切面投屏到场景 Plane"))
                    BindSliceToPlane(active);

                // 跟随 SlicingPlane 时层位置由它决定，滑条隐藏避免误解
                if (boundSliceBinder != null && boundSliceBinder.targetObject != null && !boundSliceBinder.IsFollowingSlicingPlane)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("切片层", GUILayout.Width(45f));
                    float newSlicePos = GUILayout.HorizontalSlider(boundSliceBinder.slicePosition, 0f, 1f);
                    if (!Mathf.Approximately(newSlicePos, boundSliceBinder.slicePosition))
                        boundSliceBinder.slicePosition = newSlicePos;
                    GUILayout.Label(boundSliceBinder.slicePosition.ToString("0.00"), GUILayout.Width(35f));
                    GUILayout.EndHorizontal();
                }

                GUILayout.BeginHorizontal();
                GUILayout.Label("采样率", GUILayout.Width(45f));
                float newRate = GUILayout.HorizontalSlider(samplingRate, 0.1f, 4f);
                if (!Mathf.Approximately(newRate, samplingRate))
                {
                    samplingRate = newRate;
                    active.SetSamplingRateMultiplier(newRate);
                }
                GUILayout.Label(samplingRate.ToString("0.0"), GUILayout.Width(30f));
                GUILayout.EndHorizontal();

                if (GUILayout.Button("重置传递函数 (HU 预设)"))
                    loader.ApplyHounsfieldTransferFunction(active);

                DrawSlicerPresetSection(active);

                DrawSegmentationSection(active);
            }

            GUI.DragWindow();
        }

        /// <summary>
        /// Slicer 预设区（StreamingAssets/volume_rendering_presets/*.vp.json）：
        /// 选中即应用（-MIP 预设自动切 MIP 模式），阈值偏移 slider 对应 web 的 vrShift（-1000~1000 HU）。
        /// </summary>
        private void DrawSlicerPresetSection(VolumeRenderedObject active)
        {
            if (SlicerPresetLibrary.Presets == null)
            {
                if (GUILayout.Button("加载 Slicer 预设 (vp.json)"))
                    SlicerPresetLibrary.LoadPresets();
                return;
            }

            string[] names = SlicerPresetLibrary.PresetNames;
            if (names == null || names.Length == 0)
            {
                GUILayout.Label("Slicer 预设：目录内无可解析的 .vp.json");
                return;
            }

            GUILayout.Space(6f);
            GUILayout.Label($"Slicer 预设 ({names.Length})");
            presetScrollPos = GUILayout.BeginScrollView(presetScrollPos, GUILayout.Height(120f));
            int newSelection = GUILayout.SelectionGrid(selectedPresetIndex, names, 1, GUI.skin.button);
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            GUILayout.Label("阈值偏移", GUILayout.Width(55f));
            float newShift = GUILayout.HorizontalSlider(presetHuShift, -1000f, 1000f);
            GUILayout.Label($"{presetHuShift:+0;-0;0} HU", GUILayout.Width(70f));
            GUILayout.EndHorizontal();

            bool presetChanged = newSelection != selectedPresetIndex;
            bool shiftChanged = !Mathf.Approximately(newShift, presetHuShift);
            if (presetChanged || shiftChanged)
            {
                selectedPresetIndex = newSelection;
                presetHuShift = newShift;
                if (selectedPresetIndex >= 0)
                {
                    SlicerPreset preset = SlicerPresetLibrary.Presets[selectedPresetIndex];
                    SlicerPresetLibrary.ApplyTo(active, preset, presetHuShift);
                }
            }
        }

        /// <summary>分割结构控制区：显隐开关（改 alpha 重建第二传递函数）、隔离模式、结构列表。</summary>
        private void DrawSegmentationSection(VolumeRenderedObject active)
        {
            List<SegmentationLabel> labels = active.GetSegmentationLabels();
            if (labels == null || labels.Count == 0)
                return;

            GUILayout.Space(6f);
            GUILayout.Label($"分割结构 ({labels.Count})");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全部显示"))
                SetAllSegmentVisibility(active, true);
            if (GUILayout.Button("全部隐藏"))
                SetAllSegmentVisibility(active, false);
            GUILayout.EndHorizontal();

            bool isolate = active.GetSegmentationRenderMode() == SegmentationRenderMode.Isolate;
            bool newIsolate = GUILayout.Toggle(isolate, "隔离模式（只显示分割，隐藏 CT）");
            if (newIsolate != isolate)
                active.SetSegmentationRenderMode(newIsolate
                    ? SegmentationRenderMode.Isolate
                    : SegmentationRenderMode.OverlayColour);

            segScrollPos = GUILayout.BeginScrollView(segScrollPos, GUILayout.Height(170f));
            for (int i = 0; i < labels.Count; i++)
            {
                SegmentationLabel label = labels[i];
                bool visible = label.colour.a > 0.01f;

                GUILayout.BeginHorizontal();
                // 颜色色块：隐藏时置灰
                Color prev = GUI.color;
                GUI.color = visible ? label.colour : new Color(0.4f, 0.4f, 0.4f, 1f);
                GUILayout.Label("■", GUILayout.Width(18f));
                GUI.color = prev;

                bool newState = GUILayout.Toggle(visible, label.name);
                if (newState != visible)
                    SetSegmentVisibility(active, label.id, newState);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private void SetSegmentVisibility(VolumeRenderedObject obj, int id, bool visible)
        {
            List<SegmentationLabel> labels = new List<SegmentationLabel>(obj.GetSegmentationLabels());
            int index = labels.FindIndex(l => l.id == id);
            if (index < 0)
                return;
            SegmentationLabel label = labels[index];
            Color colour = label.colour;
            colour.a = visible ? 1f : 0f;
            label.colour = colour;
            labels[index] = label;
            obj.SetSegmentationLabels(labels);
        }

        private void SetAllSegmentVisibility(VolumeRenderedObject obj, bool visible)
        {
            List<SegmentationLabel> labels = new List<SegmentationLabel>(obj.GetSegmentationLabels());
            for (int i = 0; i < labels.Count; i++)
            {
                SegmentationLabel label = labels[i];
                Color colour = label.colour;
                colour.a = visible ? 1f : 0f;
                label.colour = colour;
                labels[i] = label;
            }
            obj.SetSegmentationLabels(labels);
        }

        /// <summary>
        /// 把当前数据集的切片影像绑定到场景中的 Plane：优先复用已有的 SlicePlaneBinder，
        /// 否则在名为 "Plane" 的对象上自动挂载（也可手动在其他任意 Plane 的 Inspector 上挂 SlicePlaneBinder）。
        /// </summary>
        private void BindSliceToPlane(VolumeRenderedObject active)
        {
#if UNITY_2023_1_OR_NEWER
            SlicePlaneBinder binder = UnityEngine.Object.FindFirstObjectByType<SlicePlaneBinder>();
#else
            SlicePlaneBinder binder = UnityEngine.Object.FindObjectOfType<SlicePlaneBinder>();
#endif
            if (binder == null)
            {
                GameObject planeGo = GameObject.Find("Plane");
                if (planeGo == null)
                {
                    Debug.LogError("[VolumeViewerUI] 场景中未找到名为 \"Plane\" 的对象。请把你的 Plane 命名为 Plane，或手动在其 Inspector 上添加 SlicePlaneBinder 组件。");
                    return;
                }
                binder = planeGo.AddComponent<SlicePlaneBinder>();
            }

            binder.targetObject = active;
            boundSliceBinder = binder;
            Debug.Log($"[VolumeViewerUI] 已将 {active.name} 的切片影像绑定到 {binder.gameObject.name}（screenMode={binder.screenMode}）");
        }

        private async void ApplyRenderMode(VolumeRenderedObject obj, UnityVolumeRendering.RenderMode mode)
        {
            try
            {
                await obj.SetRenderModeAsync(mode);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
        }

        private void ToggleSlicingPlane(VolumeRenderedObject obj, bool on)
        {
            if (on)
            {
                if (!slicingPlanes.TryGetValue(obj, out SlicingPlane plane) || plane == null)
                    plane = obj.CreateSlicingPlane();
                slicingPlanes[obj] = plane;
                plane.gameObject.SetActive(true);
            }
            else if (slicingPlanes.TryGetValue(obj, out SlicingPlane plane) && plane != null)
            {
                plane.gameObject.SetActive(false);
            }
        }
    }
}
