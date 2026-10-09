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

        // ---- 调窗与十字线 ----
        private float slicePosX = 0.5f;                 // 面内 X 位置（两视图的水平轴，仅十字线联动用）
        private bool showCrosshair = true;
        private float windowWidth = 1f;                 // 窗宽（HU）
        private float windowCenter = 0.5f;              // 窗位（HU）
        private VolumeRenderedObject windowInitFor;     // 窗参数按哪个数据集初始化（切换数据集时重置为中性窗）

        // ---- 分割叠加（第二阶段）----
        private Vector2 segScrollPos;

        // ---- Slicer vp.json 预设 ----
        private Vector2 presetScrollPos;
        private int selectedPresetIndex = -1;
        private float presetHuShift = 0f;

        // ---- STL 结构模型 ----
        private Vector2 stlScrollPos;
        private float stlOpacity = 1f;

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

            // 调窗（WW/WC）：切换数据集时重置为中性窗（窗宽=全值域，窗位=中点，等效无窗）
            float dataMin = active.dataset.GetMinDataValue();
            float dataMax = active.dataset.GetMaxDataValue();
            if (windowInitFor != active)
            {
                windowInitFor = active;
                windowCenter = (dataMin + dataMax) * 0.5f;
                windowWidth = Mathf.Max(dataMax - dataMin, 1f);
            }
            sliceUIMat.SetFloat("_DataMin", dataMin);
            sliceUIMat.SetFloat("_DataMax", dataMax);
            sliceUIMat.SetFloat("_WindowCenter", windowCenter);
            sliceUIMat.SetFloat("_WindowWidth", windowWidth);

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

            float dataMin = active.dataset.GetMinDataValue();
            float dataMax = active.dataset.GetMaxDataValue();
            Event guiEvent = Event.current;

            GUILayout.BeginHorizontal();

            // 横向（轴状，沿 Z）：shader dataCoord=(uv.x, uv.y, pos) → 面内水平=X、垂直=Y
            GUILayout.BeginVertical(GUILayout.Width(210f));
            GUILayout.Label("横向切片（沿 Z 轴）· 左键定位十字线，右键拖动调窗");
            Rect axialRect = GUILayoutUtility.GetRect(200f, 200f);
            Rect axialContent = FitContentRect(axialRect, active.dataset.dimX, active.dataset.dimY);
            GUI.DrawTexture(axialContent, axialRT, ScaleMode.StretchToFill, false);
            HandleSliceViewEvents(axialContent, true, guiEvent, dataMin, dataMax);
            if (showCrosshair)
                DrawCrosshair(axialContent, slicePosX, slicePosY);
            slicePosZ = GUILayout.HorizontalSlider(slicePosZ, 0f, 1f);
            GUILayout.Label($"层位置 Z {slicePosZ:0.00}");
            GUILayout.EndVertical();

            // 纵向（冠状，沿 Y）：shader dataCoord=(uv.x, pos, uv.y) → 面内水平=X、垂直=Z
            GUILayout.BeginVertical(GUILayout.Width(210f));
            GUILayout.Label("纵向切片（沿 Y 轴）· 左键定位十字线，右键拖动调窗");
            Rect coronalRect = GUILayoutUtility.GetRect(200f, 200f);
            Rect coronalContent = FitContentRect(coronalRect, active.dataset.dimX, active.dataset.dimZ);
            GUI.DrawTexture(coronalContent, coronalRT, ScaleMode.StretchToFill, false);
            HandleSliceViewEvents(coronalContent, false, guiEvent, dataMin, dataMax);
            if (showCrosshair)
                DrawCrosshair(coronalContent, slicePosX, slicePosZ);
            slicePosY = GUILayout.HorizontalSlider(slicePosY, 0f, 1f);
            GUILayout.Label($"层位置 Y {slicePosY:0.00}");
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();

            // ---- 调窗（WW/WC）----
            GUILayout.Space(4f);
            GUILayout.BeginHorizontal();
            GUILayout.Label($"窗宽 {windowWidth:0}", GUILayout.Width(80f));
            windowWidth = GUILayout.HorizontalSlider(windowWidth, 1f, Mathf.Max((dataMax - dataMin) * 2f, 2f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"窗位 {windowCenter:0}", GUILayout.Width(80f));
            windowCenter = GUILayout.HorizontalSlider(windowCenter, dataMin, dataMax);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("肺窗"))
            {
                windowWidth = 1500f;
                windowCenter = -600f;
            }
            if (GUILayout.Button("软组织"))
            {
                windowWidth = 400f;
                windowCenter = 40f;
            }
            if (GUILayout.Button("骨窗"))
            {
                windowWidth = 1800f;
                windowCenter = 400f;
            }
            if (GUILayout.Button("复位"))
            {
                windowWidth = Mathf.Max(dataMax - dataMin, 1f);
                windowCenter = (dataMin + dataMax) * 0.5f;
            }
            bool newShowCross = GUILayout.Toggle(showCrosshair, "十字线", GUI.skin.button);
            if (newShowCross != showCrosshair)
                showCrosshair = newShowCross;
            GUILayout.EndHorizontal();

            GUI.DragWindow();
        }

        /// <summary>按内容宽高比计算图像实际绘制区（等比适配并在给定矩形内居中），保证点击换算与画面一致。</summary>
        private static Rect FitContentRect(Rect rect, float contentW, float contentH)
        {
            float scale = Mathf.Min(rect.width / contentW, rect.height / contentH);
            float w = contentW * scale;
            float h = contentH * scale;
            return new Rect(rect.x + (rect.width - w) * 0.5f, rect.y + (rect.height - h) * 0.5f, w, h);
        }

        /// <summary>
        /// 切片视图鼠标交互（folder_viewer #16/#19/#20/#17 的对应实现）：
        /// 左键点击/拖拽 = 十字线定位（轴状视图设 X/Y，冠状视图设 X/Z，联动另一视图的切层）；
        /// 右键拖拽 = 调窗（水平Δ×4→窗宽，垂直Δ×-2→窗位，与 web 一致）。
        /// </summary>
        private void HandleSliceViewEvents(Rect content, bool axial, Event guiEvent, float dataMin, float dataMax)
        {
            if (!content.Contains(guiEvent.mousePosition))
                return;

            bool leftDown = guiEvent.type == EventType.MouseDown && guiEvent.button == 0;
            bool leftDrag = guiEvent.type == EventType.MouseDrag && guiEvent.button == 0;
            bool rightDrag = guiEvent.type == EventType.MouseDrag && guiEvent.button == 1;

            if (leftDown || leftDrag)
            {
                float u = Mathf.Clamp01((guiEvent.mousePosition.x - content.x) / content.width);
                float v = Mathf.Clamp01(1f - (guiEvent.mousePosition.y - content.y) / content.height); // 屏幕向下为 v 减小
                slicePosX = u;
                if (axial)
                    slicePosY = v;
                else
                    slicePosZ = v;
                guiEvent.Use(); // 阻止 GUI.DragWindow 抢走拖拽
            }
            else if (rightDrag)
            {
                windowWidth = Mathf.Clamp(windowWidth + guiEvent.delta.x * 4f, 1f, Mathf.Max((dataMax - dataMin) * 2f, 2f));
                windowCenter = Mathf.Clamp(windowCenter - guiEvent.delta.y * 2f, dataMin, dataMax);
                guiEvent.Use();
            }
        }

        /// <summary>在视图内容区画十字线（IMGUI 无画线 API，用 1px whiteTexture 拉伸）。</summary>
        private static void DrawCrosshair(Rect content, float u, float v)
        {
            float x = content.x + u * content.width;
            float y = content.y + (1f - v) * content.height;
            Color prev = GUI.color;
            GUI.color = new Color(1f, 0.92f, 0.23f, 0.85f); // 亮黄
            GUI.DrawTexture(new Rect(content.x, y - 0.5f, content.width, 1f), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.DrawTexture(new Rect(x - 0.5f, content.y, 1f, content.height), Texture2D.whiteTexture, ScaleMode.StretchToFill, true);
            GUI.color = prev;
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

                DrawStlSection(active);

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

        /// <summary>
        /// STL 结构模型区（StreamingAssets/STL，来自 Chest_Seg 分割的同源网格）：
        /// 加载/卸载、逐结构显隐、整体不透明度；模型作为 Chest CT 体对象的子级自动对齐。
        /// </summary>
        private void DrawStlSection(VolumeRenderedObject active)
        {
            StlModelLoader stlLoader = GetComponent<StlModelLoader>();
            if (stlLoader == null)
                stlLoader = gameObject.AddComponent<StlModelLoader>();

            GUILayout.Space(6f);
            GUILayout.Label($"STL 模型 ({stlLoader.ModelCount})");

            if (stlLoader.IsLoading)
            {
                GUILayout.Label("STL 加载中（每帧一个结构，见 Console 进度）...");
                return;
            }

            if (stlLoader.ModelCount == 0)
            {
                if (GUILayout.Button("加载 STL 模型（需先加载并选中 Chest CT）"))
                    stlLoader.LoadAll(active);
                return;
            }

            GUILayout.BeginHorizontal();
            bool rootOn = stlLoader.IsRootVisible();
            bool newRootOn = GUILayout.Toggle(rootOn, "整体显示", GUI.skin.button);
            if (newRootOn != rootOn)
                stlLoader.SetRootVisible(newRootOn);
            if (GUILayout.Button("卸载"))
                stlLoader.UnloadAll();
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("全部显示"))
                stlLoader.SetAllVisible(true);
            if (GUILayout.Button("全部隐藏"))
                stlLoader.SetAllVisible(false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("不透明度", GUILayout.Width(60f));
            float newOpacity = GUILayout.HorizontalSlider(stlOpacity, 0.05f, 1f);
            GUILayout.Label(stlOpacity.ToString("0.00"), GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(newOpacity, stlOpacity))
            {
                stlOpacity = newOpacity;
                stlLoader.SetOpacity(stlOpacity);
            }

            stlScrollPos = GUILayout.BeginScrollView(stlScrollPos, GUILayout.Height(150f));
            for (int i = 0; i < stlLoader.ModelCount; i++)
            {
                bool visible = stlLoader.GetModelVisible(i);
                bool newState = GUILayout.Toggle(visible, stlLoader.GetModelName(i));
                if (newState != visible)
                    stlLoader.SetModelVisible(i, newState);
            }
            GUILayout.EndScrollView();
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
