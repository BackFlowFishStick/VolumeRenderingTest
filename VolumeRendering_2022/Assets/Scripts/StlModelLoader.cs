using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Parabox.Stl;
using UnityVolumeRendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace VolumeRenderingSample
{
    /// <summary>
    /// STL 结构模型批量加载器（对应 folder_viewer 功能 #5/#6，D-008 解除搁置后实现）。
    /// 解析核心：pb_Stl（MIT，Karl Henkel，见 Stl/LICENSE.pb_Stl.md），本类只做适配。
    ///
    /// 坐标对齐（kb/project-knowledge.md 数据事实 + 源码调研结论）：
    /// - STL 为病人 LPS 毫米坐标（TotalSegmentator 从 Chest_Seg 分割提取）；
    /// - 包的 SimpleITK 导入器做了 DICOMOrient("RSA")：dataset 轴 = R/S/A，即
    ///   x=R=-LPS.x、y=S=LPS.z、z=A=-LPS.y（rotation 字段恒为 identity）；
    /// - 体渲染 shader 以容器局部 ±0.5 空间采样纹理（uvw = vertexLocal + 0.5，无轴向翻转）；
    /// - 因此 LPS → RSA 体素索引 → 容器局部 ((idx+0.5)/dim-0.5) 即可与体数据精确对齐
    ///   （已用分割质心 vs STL 质心互校验证，胸骨偏差 <1mm）。
    /// LPS origin/spacing/sizes 从 Chest_CT.nrrd 的 ASCII 头解析（包的 VolumeDataset 不保留这些信息）。
    /// STL 只对 Chest 数据有意义（它们来自 Chest_Seg 分割），对 Lung DICOM 无对齐依据。
    /// </summary>
    public class StlModelLoader : MonoBehaviour
    {
        private struct StlModel
        {
            public string name;
            public Renderer renderer;
            public Color baseColour;
            public bool visible;
        }

        public static StlModelLoader Instance { get; private set; }

        private readonly List<StlModel> models = new List<StlModel>();
        private GameObject stlRoot;
        private bool loading;

        // Chest_CT.nrrd 头解析结果（LPS 空间）
        private Vector3 nrrdOrigin;
        private Vector3 nrrdSpacing;
        private Vector3Int nrrdSizes;

        public bool IsLoading => loading;
        public bool HasModels => models.Count > 0;
        public int ModelCount => models.Count;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>把 StreamingAssets/STL 的全部网格加载为 target（应为 Chest CT）的子模型。</summary>
        public void LoadAll(VolumeRenderedObject target)
        {
            if (!loading)
                StartCoroutine(LoadAllCoroutine(target));
        }

        private IEnumerator LoadAllCoroutine(VolumeRenderedObject target)
        {
            if (target == null || target.dataset == null)
            {
                Debug.LogError("[StlModelLoader] 目标体对象为空");
                yield break;
            }
            if (!target.name.Contains("Chest"))
            {
                Debug.LogError("[StlModelLoader] STL 模型来自 Chest_Seg 分割，只对 Chest CT 有对齐意义；请先加载 Chest CT 并选中它");
                yield break;
            }
            if (loading)
                yield break;

            loading = true;
            try
            {
                UnloadAll();

                if (!ParseChestNrrdHeader())
                    yield break;

                // 头信息与包导入后的维度核对（RSA 重定向后 dimX=nL、dimY=nS、dimZ=nP）
                if (target.dataset.dimX != nrrdSizes.x || target.dataset.dimY != nrrdSizes.z || target.dataset.dimZ != nrrdSizes.y)
                {
                    Debug.LogError($"[StlModelLoader] 维度不匹配：dataset {target.dataset.dimX}×{target.dataset.dimY}×{target.dataset.dimZ} vs NRRD {nrrdSizes.x}×{nrrdSizes.z}×{nrrdSizes.y}(LPS)，映射可能失效");
                    yield break;
                }

                string dir = Path.Combine(Application.streamingAssetsPath, "STL");
                if (!Directory.Exists(dir))
                {
                    Debug.LogError($"[StlModelLoader] 未找到 STL 目录 {dir}");
                    yield break;
                }
                List<string> files = Directory.GetFiles(dir, "*.stl").OrderBy(f => f).ToList();
                Debug.Log($"[StlModelLoader] 开始加载 {files.Count} 个 STL（平滑法线+顶点焊接，约需数十秒）...");

                stlRoot = new GameObject("STL_Models");
                stlRoot.transform.SetParent(target.volumeContainerObject.transform, false);

                int colourIndex = 0;
                foreach (string file in files)
                {
                    LoadOne(file, target, colourIndex++);
                    yield return null; // 每帧一个文件，避免长时间完全无响应
                }

                Debug.Log($"[StlModelLoader] STL 加载完成：{models.Count} 个结构 → {target.name}");
            }
            finally
            {
                loading = false;
            }
        }

        private void LoadOne(string file, VolumeRenderedObject target, int colourIndex)
        {
            string structureName = Path.GetFileNameWithoutExtension(file);
            Mesh[] meshes = Importer.Import(file, CoordinateSpace.Left, UpAxis.Y, true, IndexFormat.UInt32);
            if (meshes == null || meshes.Length == 0)
            {
                Debug.LogWarning($"[StlModelLoader] {structureName}.stl 解析失败，跳过");
                return;
            }

            // 顶点从 LPS 毫米烘成容器局部 ±0.5 空间（Grid 非均匀缩放会破坏法线，烘焙比挂 Transform 更稳）
            foreach (Mesh mesh in meshes)
            {
                Vector3[] verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++)
                    verts[i] = LpsToContainerLocal(verts[i], target.dataset);
                mesh.vertices = verts;
                mesh.RecalculateBounds();
            }

            GameObject go = new GameObject("STL_" + structureName);
            go.transform.SetParent(stlRoot.transform, false);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = meshes[0]; // UInt32 索引下不会拆分，恒为单 Mesh
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();

            Color colour = SegmentationOverlayLoader.GetStructureColour(structureName, colourIndex);
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            ConfigureMaterial(mat, colour, 1f);
            renderer.sharedMaterial = mat;

            models.Add(new StlModel { name = structureName, renderer = renderer, baseColour = colour, visible = true });
        }

        /// <summary>LPS 毫米 → 容器局部 ±0.5（经 RSA 体素索引）。</summary>
        private Vector3 LpsToContainerLocal(Vector3 lps, VolumeDataset dataset)
        {
            // DICOMOrient("RSA")：R=-L（取反），S=S，A=-P（取反）；索引 0 对应该轴最小值
            float iR = (nrrdSizes.x - 1) - (lps.x - nrrdOrigin.x) / nrrdSpacing.x;
            float jS = (lps.z - nrrdOrigin.z) / nrrdSpacing.z;
            float kA = (nrrdSizes.y - 1) - (lps.y - nrrdOrigin.y) / nrrdSpacing.y;
            return new Vector3(
                (iR + 0.5f) / dataset.dimX - 0.5f,
                (jS + 0.5f) / dataset.dimY - 0.5f,
                (kA + 0.5f) / dataset.dimZ - 0.5f);
        }

        /// <summary>解析 Chest_CT.nrrd 的 ASCII 头（space origin / space directions / sizes，LPS）。</summary>
        private bool ParseChestNrrdHeader()
        {
            string path = DatasetPathResolver.ResolveFile("Chest_CT.nrrd");
            if (path == null)
                return false;

            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (StreamReader reader = new StreamReader(fs))
            {
                // NRRD 头以空行结束
                string line;
                string sizes = null, directions = null, origin = null;
                while ((line = reader.ReadLine()) != null && line.Length > 0)
                {
                    int sep = line.IndexOf(':');
                    if (sep <= 0)
                        continue;
                    string key = line.Substring(0, sep).Trim();
                    string value = line.Substring(sep + 1).Trim();
                    if (key == "sizes") sizes = value;
                    else if (key == "space directions") directions = value;
                    else if (key == "space origin") origin = value;
                }

                if (sizes == null || directions == null || origin == null)
                {
                    Debug.LogError("[StlModelLoader] Chest_CT.nrrd 头缺少 sizes/space directions/space origin");
                    return false;
                }

                nrrdSizes = ParseVector3Int(sizes);
                nrrdOrigin = ParseVector3(origin);

                // 本数据轴向对齐（kinds 均为 domain、方向向量为对角阵）；若非对角需完整 3x3 求逆，此处直接拒绝
                string[] axisVectors = SplitVectors(directions);
                if (axisVectors.Length != 3)
                {
                    Debug.LogError($"[StlModelLoader] space directions 含 {axisVectors.Length} 个轴向量，期望 3");
                    return false;
                }
                Vector3 a0 = ParseVector3(axisVectors[0]);
                Vector3 a1 = ParseVector3(axisVectors[1]);
                Vector3 a2 = ParseVector3(axisVectors[2]);
                if (Mathf.Abs(a0.y) > 1e-6f || Mathf.Abs(a0.z) > 1e-6f
                    || Mathf.Abs(a1.x) > 1e-6f || Mathf.Abs(a1.z) > 1e-6f
                    || Mathf.Abs(a2.x) > 1e-6f || Mathf.Abs(a2.y) > 1e-6f)
                {
                    Debug.LogError("[StlModelLoader] NRRD 轴向非对角（非轴对齐数据），当前映射不适用");
                    return false;
                }
                nrrdSpacing = new Vector3(a0.x, a1.y, a2.z);
                return true;
            }
        }

        // ---- 显隐 / 透明度 / 卸载 API（供 UI 调用）----

        public string GetModelName(int index) => models[index].name;
        public bool GetModelVisible(int index) => models[index].visible;

        public void SetModelVisible(int index, bool visible)
        {
            StlModel model = models[index];
            model.visible = visible;
            models[index] = model;
            if (models[index].renderer != null)
                models[index].renderer.enabled = visible;
        }

        public void SetAllVisible(bool visible)
        {
            for (int i = 0; i < models.Count; i++)
                SetModelVisible(i, visible);
        }

        public void SetRootVisible(bool visible)
        {
            if (stlRoot != null)
                stlRoot.SetActive(visible);
        }

        public bool IsRootVisible()
        {
            return stlRoot != null && stlRoot.activeSelf;
        }

        /// <summary>整体不透明度（1=不透明；&lt;1 切到 URP 透明渲染）。</summary>
        public void SetOpacity(float alpha)
        {
            foreach (StlModel model in models)
            {
                if (model.renderer == null)
                    continue;
                ConfigureMaterial(model.renderer.sharedMaterial, model.baseColour, alpha);
            }
        }

        public void UnloadAll()
        {
            for (int i = models.Count - 1; i >= 0; i--)
            {
                if (models[i].renderer != null)
                    Destroy(models[i].renderer.gameObject);
            }
            models.Clear();
            if (stlRoot != null)
                Destroy(stlRoot);
            stlRoot = null;
        }

        // ---- 工具 ----

        private static void ConfigureMaterial(Material mat, Color colour, float alpha)
        {
            if (mat == null)
                return;
            bool transparent = alpha < 0.999f;
            if (transparent)
            {
                mat.SetFloat("_Surface", 1f); // Transparent
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_Surface", 0f); // Opaque
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.SetInt("_ZWrite", 1);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Geometry;
            }
            colour.a = alpha;
            mat.SetColor("_BaseColor", colour);
        }

        private static Vector3 ParseVector3(string text)
        {
            string inner = text.Trim().TrimStart('(').TrimEnd(')');
            string[] parts = inner.Split(',');
            return new Vector3(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture),
                float.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        private static Vector3Int ParseVector3Int(string text)
        {
            string inner = text.Trim().TrimStart('(').TrimEnd(')');
            string[] parts = inner.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            return new Vector3Int(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));
        }

        /// <summary>拆分 "（v) (v) (v)" 形式的多向量字段。</summary>
        private static string[] SplitVectors(string text)
        {
            List<string> result = new List<string>();
            int depth = 0, start = -1;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '(') { depth++; start = i + 1; }
                else if (c == ')') { depth--; if (depth == 0) result.Add(text.Substring(start, i - start)); }
            }
            return result.ToArray();
        }
    }
}
