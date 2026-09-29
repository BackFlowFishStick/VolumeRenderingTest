using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityVolumeRendering;
using UnityEngine;
// Unity 6 的 UnityEngine 命名空间也存在同名类型，显式消歧
using TransferFunction = UnityVolumeRendering.TransferFunction;

namespace VolumeRenderingSample
{
    /// <summary>
    /// 运行时体渲染引导。通过 [RuntimeInitializeOnLoadMethod] 自动挂载，无需改场景。
    /// 两条加载链路：
    /// - Lung DICOM 序列（Assets/Resources/lung，编辑器下文件 IO 直读）
    /// - Chest CT NRRD（Chest_Reconstruction.mrb 解包产物，位于 StreamingAssets/Datasets，依赖 SimpleITK 后端）
    /// </summary>
    public class RuntimeVolumeLoader : MonoBehaviour
    {
        public static RuntimeVolumeLoader Instance { get; private set; }

        private readonly List<VolumeRenderedObject> loadedObjects = new List<VolumeRenderedObject>();

        /// <summary>已加载的体对象列表（可能包含已被销毁的条目，读取时需判空）。</summary>
        public IReadOnlyList<VolumeRenderedObject> LoadedObjects => loadedObjects;

        public VolumeRenderedObject ActiveObject { get; private set; }

        public bool IsBusy { get; private set; }
        public float Progress01 { get; private set; }
        public string ProgressDescription { get; private set; } = "";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // 2023.1 起推荐 FindFirstObjectByType；2022.3 回退 FindObjectOfType，保持双版本兼容
#if UNITY_2023_1_OR_NEWER
            if (UnityEngine.Object.FindFirstObjectByType<RuntimeVolumeLoader>() != null)
#else
            if (UnityEngine.Object.FindObjectOfType<RuntimeVolumeLoader>() != null)
#endif
                return;

            GameObject root = new GameObject("VolumeRuntimeRoot");
            root.AddComponent<RuntimeVolumeLoader>();
            root.AddComponent<VolumeViewerUI>();

            Camera cam = Camera.main;
            if (cam != null && cam.GetComponent<CameraController>() == null)
                cam.gameObject.AddComponent<CameraController>();
        }

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>加载 lung 目录下的 DICOM 序列（320 张切片，内置 openDICOM 后端，无需 SimpleITK）。</summary>
        public async void LoadLungDicomAsync()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                string dir = DatasetPathResolver.ResolveDirectory("lung");
                if (dir == null)
                    return;

                List<string> files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(p => p.EndsWith(".dcm", StringComparison.InvariantCultureIgnoreCase)
                             || p.EndsWith(".dicom", StringComparison.InvariantCultureIgnoreCase)
                             || p.EndsWith(".dicm", StringComparison.InvariantCultureIgnoreCase))
                    .ToList();
                if (files.Count == 0)
                {
                    Debug.LogError($"[RuntimeVolumeLoader] {dir} 下没有 DICOM 文件");
                    return;
                }

                Debug.Log($"[RuntimeVolumeLoader] 开始导入 DICOM 序列，共 {files.Count} 张切片（首次解析约需数十秒）...");
                IImageSequenceImporter importer = ImporterFactory.CreateImageSequenceImporter(ImageSequenceFormat.DICOM);
                ImageSequenceImportSettings settings = new ImageSequenceImportSettings
                {
                    progressHandler = new LoadingProgress(this, "解析 DICOM"),
                };

                IEnumerable<IImageSequenceSeries> seriesList = await importer.LoadSeriesAsync(files, settings);
                int index = 0;
                foreach (IImageSequenceSeries series in seriesList)
                {
                    VolumeDataset dataset = await importer.ImportSeriesAsync(series, settings);
                    if (dataset == null)
                        continue;
                    await CreateVolumeObjectAsync(dataset, $"Lung_DICOM_{index++}", settings.progressHandler);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                IsBusy = false;
                Progress01 = 0f;
                ProgressDescription = "";
            }
        }

        /// <summary>加载胸部 CT（Chest_Reconstruction.mrb 解包出的 input_ct.nrrd，NRRD 需要 SimpleITK 后端）。</summary>
        public async void LoadChestCtAsync()
        {
            if (IsBusy)
                return;
            IsBusy = true;
            try
            {
                string path = DatasetPathResolver.ResolveFile("Chest_CT.nrrd");
                if (path == null)
                {
                    Debug.LogError("[RuntimeVolumeLoader] 未找到 Chest_CT.nrrd。请先在编辑器执行菜单 Tools/Volume Rendering/Extract Chest MRB 解包 Chest_Reconstruction.mrb。");
                    return;
                }

                IImageFileImporter importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NRRD);
                if (importer == null)
                {
                    Debug.LogError("[RuntimeVolumeLoader] NRRD 导入器不可用：NRRD 依赖 SimpleITK 后端。请在编辑器启用 Volume Rendering → Settings → Enable SimpleITK 后重试。");
                    return;
                }

                Debug.Log("[RuntimeVolumeLoader] 开始导入 Chest CT NRRD（约 83MB）...");
                VolumeDataset dataset = await importer.ImportAsync(path);
                if (dataset == null)
                {
                    Debug.LogError("[RuntimeVolumeLoader] Chest NRRD 导入失败");
                    return;
                }
                VolumeRenderedObject chestObj = await CreateVolumeObjectAsync(dataset, "Chest_CT", null);

                // 第二阶段：分割叠加（Chest_Seg.nrrd + ReconstructionLabels.csv），失败不影响 CT 主体
                try
                {
                    await SegmentationOverlayLoader.LoadAsync(chestObj);
                    if (chestObj.GetOverlayType() != UnityVolumeRendering.OverlayType.None)
                        ApplyHounsfieldTransferFunction(chestObj, true); // 分割就绪后切换教学预设（外壳透明、器官色突出）
                }
                catch (Exception e)
                {
                    Debug.LogError($"[RuntimeVolumeLoader] 分割叠加加载失败：{e.Message}");
                    Debug.LogException(e);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                IsBusy = false;
                Progress01 = 0f;
                ProgressDescription = "";
            }
        }

        /// <summary>单选显示某个体对象（两份数据都在原点，同时显示会重叠）。</summary>
        public void SetActiveObject(VolumeRenderedObject obj)
        {
            ActiveObject = obj;
            foreach (VolumeRenderedObject loaded in loadedObjects)
            {
                if (loaded != null)
                    loaded.gameObject.SetActive(loaded == obj);
            }
        }

        /// <summary>强制使用教学版预设（外壳全透明、器官色突出、骨骼半透明参照）。传入的 teachingPreset 由本重载固定为 true。</summary>
        public void ApplyHounsfieldTransferFunction(VolumeRenderedObject obj)
        {
            ApplyHounsfieldTransferFunction(obj, true);
        }

        /// <summary>
        /// 按 CT 的 Hounsfield 值建立 1D 传递函数预设。
        /// teachingPreset=true：脂肪/皮肤/软组织近乎全透明（分割覆盖处由结构色接管），骨骼保留半透明作解剖参照；
        /// teachingPreset=false：标准版（软组织低不透明、骨骼高不透明）。
        /// </summary>
        public void ApplyHounsfieldTransferFunction(VolumeRenderedObject obj, bool teachingPreset)
        {
            if (obj == null || obj.dataset == null)
                return;

            VolumeDataset dataset = obj.dataset;
            float min = dataset.GetMinDataValue();
            float max = dataset.GetMaxDataValue();
            float range = Mathf.Max(max - min, 1e-5f);
            float Norm(float hu) => Mathf.Clamp01((hu - min) / range);

            TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
            tf.colourControlPoints.Clear();
            tf.alphaControlPoints.Clear();

            tf.AddControlPoint(new TFColourControlPoint(Norm(-1000f), new Color(0.10f, 0.14f, 0.22f)));
            tf.AddControlPoint(new TFColourControlPoint(Norm(0f),     teachingPreset ? new Color(0.50f, 0.55f, 0.62f) : new Color(0.58f, 0.58f, 0.60f)));
            tf.AddControlPoint(new TFColourControlPoint(Norm(150f),   teachingPreset ? new Color(0.66f, 0.66f, 0.70f) : new Color(0.76f, 0.56f, 0.42f)));
            tf.AddControlPoint(new TFColourControlPoint(Norm(800f),   new Color(0.92f, 0.90f, 0.86f)));
            tf.AddControlPoint(new TFColourControlPoint(Norm(3000f),  Color.white));

            if (teachingPreset)
            {
                // 教学版（只看分割色）：CT 本体（外壳 + 骨骼）全部 alpha=0，
                // 画面上仅剩分割叠加的彩色结构（DVR 下分割体素整色替换、alpha=1）。
                // 如需恢复骨骼灰色参照，把 700/1200/3000 三行调回 0.08/0.25/0.60。
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(-1000f), 0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(-300f),  0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(-100f),  0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(0f),     0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(80f),    0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(300f),   0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(700f),   0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(1200f),  0.000f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(3000f),  0.000f));
            }
            else
            {
                // 标准版：空气透明、软组织低不透明、骨骼高不透明（适合无分割的 Lung 数据）
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(-1000f), 0.00f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(-300f),  0.00f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(0f),     0.03f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(60f),    0.12f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(150f),   0.20f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(300f),   0.35f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(1200f),  0.85f));
                tf.AddControlPoint(new TFAlphaControlPoint(Norm(3000f),  0.95f));
            }

            // 关键：CreateTransferFunction() 在创建时已用默认控制点预生成了纹理缓存，
            // GetTexture() 只在缓存为 null 时才重新生成——不清掉缓存，下面的控制点永远不会生效。
            tf.GenerateTexture();

            obj.SetTransferFunction(tf);

            // 教学版按 DVR 设计：等值面模式在命中表面时强制 col.a=1（无视 TF alpha）、
            // MIP 只输出白色×强度（不采样 TF）——这两种模式下改 alpha 不会有任何视觉变化。
            // 因此应用教学预设时强制切回 DVR，避免"改了 alpha 没反应"的困惑。
            if (teachingPreset && obj.GetRenderMode() != UnityVolumeRendering.RenderMode.DirectVolumeRendering)
            {
                Debug.Log($"[RuntimeVolumeLoader] {obj.name} 当前处于 {obj.GetRenderMode()} 模式（该模式不读取 TF alpha），已切回 DVR 以呈现教学预设");
                obj.SetRenderMode(UnityVolumeRendering.RenderMode.DirectVolumeRendering);
            }
            Debug.Log($"[RuntimeVolumeLoader] 已为 {obj.name} 应用{(teachingPreset ? "教学版" : "标准版")}传递函数预设");
        }

        private async Task<VolumeRenderedObject> CreateVolumeObjectAsync(VolumeDataset dataset, string objectName, IProgressHandler progress)
        {
            VolumeRenderedObject obj = await VolumeObjectFactory.CreateObjectAsync(dataset, progress);
            obj.name = objectName;
            ApplyHounsfieldTransferFunction(obj);

            // 首个加载的自动显示并取景
            if (ActiveObject == null)
            {
                ActiveObject = obj;
                FrameCamera(obj);
            }
            obj.gameObject.SetActive(obj == ActiveObject);

            loadedObjects.Add(obj);
            return obj;
        }

        private void FrameCamera(VolumeRenderedObject obj)
        {
            Camera cam = Camera.main;
            if (cam == null || obj.meshRenderer == null)
                return;

            Bounds bounds = obj.meshRenderer.bounds;
            Vector3 center = bounds.center;
            float distance = bounds.size.magnitude * 1.2f;
            cam.transform.position = center + new Vector3(0.4f, 0.35f, -1.0f).normalized * distance;
            cam.transform.LookAt(center);
        }

        /// <summary>
        /// 进度回调：导入器在后台线程调用，只做字段赋值，由 OnGUI 读取。
        /// </summary>
        private class LoadingProgress : IProgressHandler
        {
            private readonly RuntimeVolumeLoader owner;
            private readonly string stageName;

            public LoadingProgress(RuntimeVolumeLoader owner, string stageName)
            {
                this.owner = owner;
                this.stageName = stageName;
            }

            public void StartStage(float weight, string description = "") { }

            public void EndStage() { }

            public void ReportProgress(float progress, string description = "")
            {
                owner.Progress01 = Mathf.Clamp01(progress);
                owner.ProgressDescription = string.IsNullOrEmpty(description) ? stageName : $"{stageName}：{description}";
            }

            public void ReportProgress(int currentStep, int totalSteps, string description = "")
            {
                ReportProgress(totalSteps > 0 ? (float)currentStep / totalSteps : 0f, description);
            }

            public void Fail() { }
        }
    }
}
