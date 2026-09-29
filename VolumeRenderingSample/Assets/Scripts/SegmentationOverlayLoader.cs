using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityVolumeRendering;
using UnityEngine;

namespace VolumeRenderingSample
{
    /// <summary>
    /// 分割叠加（第二阶段）：加载 Chest_Seg.nrrd（TotalSegmentator 标签体，体素值=结构编号），
    /// 解析 ReconstructionLabels.csv 得到编号→结构名对照，
    /// 然后对胸部体对象调 AddSegmentation 完成彩色叠加。
    ///
    /// 说明：
    /// - 标签只保留数据中实际出现的结构（后台扫描一遍体素，约 84M 值）；
    /// - 颜色不用 CSV 里的统一灰色，而是常用结构配色 + 其余按黄金比例色相生成，保证教学时区分度高；
    /// - 单结构显隐由 UI 改 SegmentationLabel.colour.a 后 SetSegmentationLabels 实现（包机制）。
    /// </summary>
    public static class SegmentationOverlayLoader
    {
        // 高频教学结构的固定配色（其余结构自动生成稳定色相）
        private static readonly Dictionary<string, Color> CuratedColors = new Dictionary<string, Color>
        {
            { "heart",                  new Color(0.90f, 0.10f, 0.12f) },
            { "aorta",                  new Color(0.95f, 0.35f, 0.15f) },
            { "lung_upper_lobe_left",   new Color(0.25f, 0.55f, 0.95f) },
            { "lung_lower_lobe_left",   new Color(0.20f, 0.45f, 0.85f) },
            { "lung_upper_lobe_right",  new Color(0.35f, 0.65f, 0.95f) },
            { "lung_lower_lobe_right",  new Color(0.30f, 0.55f, 0.85f) },
            { "lung_middle_lobe_right", new Color(0.45f, 0.72f, 0.95f) },
            { "trachea",                new Color(0.60f, 0.85f, 0.40f) },
            { "esophagus",              new Color(0.85f, 0.60f, 0.30f) },
            { "spinal_cord",            new Color(0.95f, 0.85f, 0.30f) },
            { "sternum",                new Color(0.95f, 0.95f, 0.55f) },
            { "liver",                  new Color(0.75f, 0.30f, 0.20f) },
        };

        /// <summary>加载分割数据并叠加到目标体对象上。失败只记日志，不影响 CT 主体显示。</summary>
        public static async Task LoadAsync(VolumeRenderedObject target)
        {
            string segPath = DatasetPathResolver.ResolveFile("Chest_Seg.nrrd");
            if (segPath == null)
            {
                Debug.LogWarning("[Segmentation] 未找到 Chest_Seg.nrrd，跳过分割叠加");
                return;
            }

            string csvPath = DatasetPathResolver.ResolveFile("ReconstructionLabels.csv");
            if (csvPath == null)
            {
                Debug.LogWarning("[Segmentation] 未找到 ReconstructionLabels.csv，跳过分割叠加");
                return;
            }

            IImageFileImporter importer = ImporterFactory.CreateImageFileImporter(ImageFileFormat.NRRD);
            if (importer == null)
            {
                Debug.LogError("[Segmentation] NRRD 导入器不可用（需要 SimpleITK），跳过分割叠加");
                return;
            }

            Debug.Log("[Segmentation] 开始加载分割标注体 ...");
            VolumeDataset segDataset = await importer.ImportAsync(segPath);
            if (segDataset == null)
            {
                Debug.LogError("[Segmentation] Chest_Seg.nrrd 导入失败");
                return;
            }

            if (target.dataset.data.Length != segDataset.data.Length)
            {
                Debug.LogError($"[Segmentation] 分割体与 CT 尺寸不一致（{segDataset.dimX}×{segDataset.dimY}×{segDataset.dimZ}），跳过");
                return;
            }

            List<SegmentationLabel> allLabels = ParseLabelsCsv(csvPath);

            // 后台扫描数据中实际出现的结构编号（数组约 84M 值，主线程扫会卡顿）
            HashSet<int> usedIds = await Task.Run(() =>
            {
                HashSet<int> ids = new HashSet<int>();
                float[] data = segDataset.data;
                for (int i = 0; i < data.Length; i++)
                {
                    int value = Mathf.RoundToInt(data[i]);
                    if (value > 0)
                        ids.Add(value);
                }
                return ids;
            });

            List<SegmentationLabel> usedLabels = allLabels.Where(l => usedIds.Contains(l.id)).ToList();
            Debug.Log($"[Segmentation] 数据中共 {usedIds.Count} 个结构（CSV 定义 {allLabels.Count} 个），开始叠加");

            target.AddSegmentation(segDataset, usedLabels);
            Debug.Log($"[Segmentation] 分割叠加完成：{target.name} 上挂载 {usedLabels.Count} 个结构");
        }

        /// <summary>
        /// 解析 TotalSegmentator 风格的标签 CSV（列：LabelValue, Name, Color_R/G/B/A, ...）。
        /// 颜色不取 CSV（全为统一灰色）：优先用 CuratedColors，其余按黄金比例色相生成稳定配色。
        /// </summary>
        private static List<SegmentationLabel> ParseLabelsCsv(string csvPath)
        {
            List<SegmentationLabel> labels = new List<SegmentationLabel>();
            string[] lines = File.ReadAllLines(csvPath);
            int colorIndex = 0;

            for (int lineIdx = 1; lineIdx < lines.Length; lineIdx++) // 第 0 行是表头
            {
                string line = lines[lineIdx].Trim();
                if (line.Length == 0)
                    continue;

                string[] fields = line.Split(',');
                if (fields.Length < 2)
                    continue;

                if (!int.TryParse(fields[0].Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 0)
                    continue;

                string name = fields[1].Trim('"');

                Color colour;
                if (!CuratedColors.TryGetValue(name, out colour))
                {
                    // 黄金比例色相：相邻结构颜色拉开，且顺序稳定
                    colour = Color.HSVToRGB((colorIndex * 0.618034f) % 1f, 0.65f, 0.95f);
                    colorIndex++;
                }

                labels.Add(new SegmentationLabel { id = id, name = name, colour = colour });
            }

            return labels;
        }
    }
}
