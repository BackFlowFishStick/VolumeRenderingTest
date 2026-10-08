using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityVolumeRendering;
using UnityEngine;
// TransferFunction 与工程历史脚本同名，显式消歧（kb/project-knowledge.md §3 编译歧义坑）
using TransferFunction = UnityVolumeRendering.TransferFunction;

namespace VolumeRenderingSample
{
    /// <summary>
    /// 解析后的 3D Slicer 预设（.vp.json，volume-property schema v1.0.0）。
    /// 控制点保留原始 HU 标量值，应用时按当前数据集值域归一化（kb/project-knowledge.md §1.4）。
    /// </summary>
    public class SlicerPreset
    {
        public string name;
        public string interpolationType = "linear";
        public readonly List<ColourPoint> colourPoints = new List<ColourPoint>();
        public readonly List<OpacityPoint> opacityPoints = new List<OpacityPoint>();

        public bool IsMip => name.EndsWith("-MIP", StringComparison.OrdinalIgnoreCase);

        public struct ColourPoint { public float hu; public Color colour; }
        public struct OpacityPoint { public float hu; public float alpha; }
    }

    /// <summary>
    /// .vp.json 预设加载与应用（对应 folder_viewer 功能 #10 预设导入 + #9 MIP 自动切换）。
    /// 数据来源：StreamingAssets/volume_rendering_presets/*.vp.json（32 个：31 内置 + 1 快照）。
    ///
    /// 格式契约（kb/agent-a/findings-folder-viewer.md §3.1）：
    /// - 只取 volumeProperties[0].components[0]（与 web 一致）；
    /// - rgbTransferFunction.points → 颜色控制点；scalarOpacity.points → alpha 控制点；
    /// - midpoint/sharpness 强制 0.5/0（线性控制点），gradientOpacity/lighting 等字段不映射；
    /// - x 归一化用数据集实际值域 (x-min)/(max-min)，Slicer 对控制点区间外取端点钳制语义，
    ///   因此显式补 0/1 端点，避免包 GenerateTexture() 注入默认的 (0,alpha=0)/(1,alpha=1)。
    /// 注意 kb §1.1：CreateTransferFunction() 预生成默认 TF 纹理缓存，填充控制点后必须显式 GenerateTexture()。
    /// </summary>
    public static class SlicerPresetLibrary
    {
        private const string PresetFolderName = "volume_rendering_presets";
        /// <summary>folder_viewer 实测口径：31 个内置预设 + 1 个当前参数快照。</summary>
        public const int ExpectedPresetCount = 32;

        private static List<SlicerPreset> presets;
        private static string[] presetNames;

        public static IReadOnlyList<SlicerPreset> Presets => presets;
        public static string[] PresetNames => presetNames;

        /// <summary>扫描预设目录并解析全部 .vp.json（跳过汇总文件 presets-all.json，同名原生优先）。</summary>
        public static bool LoadPresets(bool forceReload = false)
        {
            if (!forceReload && presets != null)
                return true;

            string dir = Path.Combine(Application.streamingAssetsPath, PresetFolderName);
            if (!Directory.Exists(dir))
            {
                Debug.LogError($"[SlicerPresetLibrary] 未找到预设目录 {dir}");
                return false;
            }

            List<SlicerPreset> parsed = new List<SlicerPreset>();
            Dictionary<string, SlicerPreset> byName = new Dictionary<string, SlicerPreset>();
            foreach (string file in Directory.EnumerateFiles(dir, "*.vp.json", SearchOption.TopDirectoryOnly).OrderBy(f => f))
            {
                if (ParseFile(file, out SlicerPreset preset))
                {
                    // 同名去重：目录内均为原生 .vp.json，理论上无重名；保留先解析者并告警以防数据异常
                    if (byName.TryGetValue(preset.name, out SlicerPreset existing))
                        Debug.LogWarning($"[SlicerPresetLibrary] 预设重名，忽略后者：{preset.name}（{Path.GetFileName(file)}）");
                    else
                    {
                        byName.Add(preset.name, preset);
                        parsed.Add(preset);
                    }
                }
            }

            presets = parsed;
            presetNames = parsed.Select(p => p.name).ToArray();
            Debug.Log($"[SlicerPresetLibrary] 已解析 {presets.Count} 个预设（期望 {ExpectedPresetCount}，含 current-volume-property 快照）");
            return presets.Count > 0;
        }

        /// <summary>
        /// 把预设应用到体对象：填充 1D 传递函数、显式重建纹理、按名称自动切换 DVR/MIP。
        /// huShift 为阈值偏移（HU，对应 web 的 vrShift），正数向亮侧平移查表位置。
        /// </summary>
        public static bool ApplyTo(VolumeRenderedObject obj, SlicerPreset preset, float huShift = 0f)
        {
            if (obj == null || obj.dataset == null)
            {
                Debug.LogError("[SlicerPresetLibrary] 目标体对象或数据集为空");
                return false;
            }
            if (preset == null || preset.colourPoints.Count == 0 || preset.opacityPoints.Count == 0)
            {
                Debug.LogError($"[SlicerPresetLibrary] 预设无效：{(preset?.name ?? "null")}");
                return false;
            }

            VolumeDataset dataset = obj.dataset;
            float min = dataset.GetMinDataValue();
            float max = dataset.GetMaxDataValue();
            if (max - min < 1e-5f)
            {
                Debug.LogError($"[SlicerPresetLibrary] {obj.name} 数据值域过小（[{min}, {max}]），无法归一化");
                return false;
            }
            // huShift：查表位置平移 = 标量值方向反向平移控制点（等价于 web 在 transfer() 中平移查表下标）
            Func<float, float> norm = hu => Mathf.Clamp01((hu + huShift - min) / (max - min));

            // 清掉旧 TF 对象（连同其纹理缓存）再重建，杜绝任何旧纹理残留；同时避免 ScriptableObject 泄漏
            if (obj.transferFunction != null)
                UnityEngine.Object.Destroy(obj.transferFunction);

            TransferFunction tf = TransferFunctionDatabase.CreateTransferFunction();
            tf.colourControlPoints.Clear();
            tf.alphaControlPoints.Clear();

            AddClampedColourPoints(tf, preset, norm);
            AddClampedOpacityPoints(tf, preset, norm);

            // 关键：清掉 CreateTransferFunction() 的默认纹理缓存，否则上面的控制点不生效（kb §1.1）
            tf.GenerateTexture();
            obj.SetTransferFunction(tf);

            // 与 web 一致：-MIP 后缀预设自动切 MIP；普通预设切回 DVR（只有 DVR 采样 TF alpha，kb §1.2）
            UnityVolumeRendering.RenderMode targetMode = preset.IsMip
                ? UnityVolumeRendering.RenderMode.MaximumIntensityProjectipon
                : UnityVolumeRendering.RenderMode.DirectVolumeRendering;
            if (obj.GetRenderMode() != targetMode)
            {
                obj.SetRenderMode(targetMode);
                Debug.Log($"[SlicerPresetLibrary] 预设 {preset.name} → 已切换渲染模式 {targetMode}");
            }
            else if (preset.IsMip && Mathf.Abs(huShift) > 0.01f)
            {
                // MIP 输出 float4(1,1,1,maxDensity)，完全不采样 TF（kb 1.2）——偏移在 MIP 下必然无视觉效果
                Debug.LogWarning("[SlicerPresetLibrary] 当前处于 MIP 模式（不采样传递函数），阈值偏移不会有视觉效果；请先选一个非 -MIP 预设切回 DVR 再试偏移");
            }

            Debug.Log($"[SlicerPresetLibrary] 已应用预设 {preset.name}（颜色 {preset.colourPoints.Count} 点 / alpha {preset.opacityPoints.Count} 点，shift {huShift:+0;-0;0} HU）");
            return true;
        }

        /// <summary>
        /// 颜色控制点：归一化 + 排序去重 + 补 0/1 端点。
        /// Slicer 对控制点区间外取端点钳制；不显式补端点的话，包 GenerateTexture() 会注入
        /// 默认的 (0, Color.white) / (1, Color.white)，超出预设设计值域的部分会变成白色。
        /// </summary>
        private static void AddClampedColourPoints(TransferFunction tf, SlicerPreset preset, Func<float, float> norm)
        {
            List<SlicerPreset.ColourPoint> points = DeduplicateByX(preset.colourPoints
                .Select(p => new SlicerPreset.ColourPoint { hu = norm(p.hu), colour = p.colour })
                .OrderBy(p => p.hu)
                .ToList(), p => p.hu);
            foreach (SlicerPreset.ColourPoint p in points)
                tf.AddControlPoint(new TFColourControlPoint(p.hu, p.colour));
            if (points[0].hu > 0f)
                tf.colourControlPoints.Insert(0, new TFColourControlPoint(0f, points[0].colour));
            if (points[points.Count - 1].hu < 1f)
                tf.colourControlPoints.Add(new TFColourControlPoint(1f, points[points.Count - 1].colour));
        }

        private static void AddClampedOpacityPoints(TransferFunction tf, SlicerPreset preset, Func<float, float> norm)
        {
            List<SlicerPreset.OpacityPoint> points = DeduplicateByX(preset.opacityPoints
                .Select(p => new SlicerPreset.OpacityPoint { hu = norm(p.hu), alpha = p.alpha })
                .OrderBy(p => p.hu)
                .ToList(), p => p.hu);
            foreach (SlicerPreset.OpacityPoint p in points)
                tf.AddControlPoint(new TFAlphaControlPoint(p.hu, p.alpha));
            if (points[0].hu > 0f)
                tf.alphaControlPoints.Insert(0, new TFAlphaControlPoint(0f, points[0].alpha));
            if (points[points.Count - 1].hu < 1f)
                tf.alphaControlPoints.Add(new TFAlphaControlPoint(1f, points[points.Count - 1].alpha));
        }

        /// <summary>
        /// 归一化钳制可能使多个点落到同一 x（预设值域超出数据值域时聚集在 0 或 1），
        /// 同 x 重复点会让包 GenerateTexture() 的线性插值除零产生 NaN——同 x 只保留最后一个点。
        /// </summary>
        private static List<T> DeduplicateByX<T>(List<T> sortedPoints, Func<T, float> getX)
        {
            List<T> result = new List<T>(sortedPoints.Count);
            foreach (T point in sortedPoints)
            {
                if (result.Count > 0 && Mathf.Approximately(getX(result[result.Count - 1]), getX(point)))
                    result[result.Count - 1] = point;
                else
                    result.Add(point);
            }
            return result;
        }

        // ---- JSON DTO（JsonUtility 只映射 public 字段；未知字段如 @schema 自动忽略）----

        [Serializable]
        private class VpFile { public VpVolumeProperty[] volumeProperties; }

        [Serializable]
        private class VpVolumeProperty
        {
            public float[] effectiveRange;
            public string interpolationType;
            public VpComponent[] components;
        }

        [Serializable]
        private class VpComponent
        {
            public VpColourFunction rgbTransferFunction;
            public VpPiecewiseFunction scalarOpacity;
            public string interpolationType;
        }

        [Serializable]
        private class VpColourFunction { public VpColourPoint[] points; }

        [Serializable]
        private class VpColourPoint
        {
            public float x;
            public float[] color;
            public float midpoint;
            public float sharpness;
        }

        [Serializable]
        private class VpPiecewiseFunction { public VpScalarPoint[] points; }

        [Serializable]
        private class VpScalarPoint
        {
            public float x;
            public float y;
            public float midpoint;
            public float sharpness;
        }

        /// <summary>解析单个 .vp.json；校验规则与 web 的 normalizePreset() 一致（RGB、有限值、x 严格递增）。</summary>
        public static bool ParseFile(string path, out SlicerPreset preset)
        {
            preset = null;
            string fileName = Path.GetFileName(path);
            try
            {
                VpFile file = JsonUtility.FromJson<VpFile>(File.ReadAllText(path));
                if (file?.volumeProperties == null || file.volumeProperties.Length == 0)
                {
                    Debug.LogError($"[SlicerPresetLibrary] {fileName}: 缺少 volumeProperties");
                    return false;
                }
                if (file.volumeProperties.Length > 1)
                    Debug.LogWarning($"[SlicerPresetLibrary] {fileName}: 含 {file.volumeProperties.Length} 个 volumeProperties，仅取 [0]（与 web 一致）");
                VpVolumeProperty prop = file.volumeProperties[0];
                if (prop.components == null || prop.components.Length == 0)
                {
                    Debug.LogError($"[SlicerPresetLibrary] {fileName}: volumeProperties[0] 缺少 components");
                    return false;
                }
                if (prop.components.Length > 1)
                    Debug.LogWarning($"[SlicerPresetLibrary] {fileName}: 含 {prop.components.Length} 个 components，仅取 [0]（与 web 一致）");
                VpComponent component = prop.components[0];

                if (component.rgbTransferFunction?.points == null || component.rgbTransferFunction.points.Length == 0
                    || component.scalarOpacity?.points == null || component.scalarOpacity.points.Length == 0)
                {
                    Debug.LogError($"[SlicerPresetLibrary] {fileName}: 颜色或 alpha 控制点为空");
                    return false;
                }

                preset = new SlicerPreset { name = Path.GetFileNameWithoutExtension(fileName).Replace(".vp", "") };
                // interpolationType 组件级字段优先，回退 volumeProperty 级（两种出现位置都见过）
                preset.interpolationType = FirstNonEmpty(component.interpolationType, prop.interpolationType);
                if (!string.Equals(preset.interpolationType, "linear", StringComparison.OrdinalIgnoreCase))
                    Debug.LogWarning($"[SlicerPresetLibrary] {fileName}: interpolationType={preset.interpolationType}，包仅支持三线性，按 linear 处理");

                if (!FillColourPoints(preset, component.rgbTransferFunction.points, fileName))
                    return false;
                if (!FillOpacityPoints(preset, component.scalarOpacity.points, fileName))
                    return false;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[SlicerPresetLibrary] 解析 {fileName} 失败：{e.Message}");
                preset = null;
                return false;
            }
        }

        private static bool FillColourPoints(SlicerPreset preset, VpColourPoint[] points, string fileName)
        {
            float lastX = float.NegativeInfinity;
            foreach (VpColourPoint point in points)
            {
                if (!IsFinite(point.x) || point.color == null || point.color.Length < 3
                    || point.color.Take(3).Any(c => !IsFinite(c)))
                {
                    Debug.LogError($"[SlicerPresetLibrary] {fileName}: 颜色控制点非法（x={point.x}，color 元素数 {point.color?.Length ?? 0}）");
                    return false;
                }
                if (point.x < lastX && !Mathf.Approximately(point.x, lastX))
                {
                    Debug.LogError($"[SlicerPresetLibrary] {fileName}: 颜色控制点 x 回退（{lastX} → {point.x}）");
                    return false;
                }
                if (Mathf.Approximately(point.x, lastX) && preset.colourPoints.Count > 0)
                {
                    // Slicer 导出常见双精度尾差重复点（x 与 x·(1+ε)，值相同）——归并为一点，保留后者
                    preset.colourPoints[preset.colourPoints.Count - 1] = new SlicerPreset.ColourPoint
                    {
                        hu = point.x,
                        colour = new Color(point.color[0], point.color[1], point.color[2], 1f),
                    };
                    lastX = point.x;
                    continue;
                }
                // midpoint/sharpness 仅接受默认值（web 同契约）；非默认时警告但接受（包 1D TF 本就是线性插值）
                WarnIfNonLinear(point.midpoint, point.sharpness, fileName, "颜色");
                lastX = point.x;
                preset.colourPoints.Add(new SlicerPreset.ColourPoint
                {
                    hu = point.x,
                    colour = new Color(point.color[0], point.color[1], point.color[2], 1f),
                });
            }
            return true;
        }

        private static bool FillOpacityPoints(SlicerPreset preset, VpScalarPoint[] points, string fileName)
        {
            float lastX = float.NegativeInfinity;
            foreach (VpScalarPoint point in points)
            {
                if (!IsFinite(point.x) || !IsFinite(point.y) || point.y < 0f || point.y > 1f)
                {
                    Debug.LogError($"[SlicerPresetLibrary] {fileName}: alpha 控制点非法（x={point.x}，y={point.y}）");
                    return false;
                }
                if (point.x < lastX && !Mathf.Approximately(point.x, lastX))
                {
                    Debug.LogError($"[SlicerPresetLibrary] {fileName}: alpha 控制点 x 回退（{lastX} → {point.x}）");
                    return false;
                }
                if (Mathf.Approximately(point.x, lastX) && preset.opacityPoints.Count > 0)
                {
                    // 同上：尾差重复点归并，保留后者
                    preset.opacityPoints[preset.opacityPoints.Count - 1] = new SlicerPreset.OpacityPoint { hu = point.x, alpha = point.y };
                    lastX = point.x;
                    continue;
                }
                WarnIfNonLinear(point.midpoint, point.sharpness, fileName, "alpha");
                lastX = point.x;
                preset.opacityPoints.Add(new SlicerPreset.OpacityPoint { hu = point.x, alpha = point.y });
            }
            return true;
        }

        private static void WarnIfNonLinear(float midpoint, float sharpness, string fileName, string kind)
        {
            if (!Mathf.Approximately(midpoint, 0.5f) || !Mathf.Approximately(sharpness, 0f))
                Debug.LogWarning($"[SlicerPresetLibrary] {fileName}: {kind}控制点含非线性 midpoint/sharpness（{midpoint}/{sharpness}），按线性处理");
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static string FirstNonEmpty(string a, string b) => !string.IsNullOrEmpty(a) ? a : (b ?? "linear");
    }
}
