using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VolumeRenderingSample
{
    /// <summary>
    /// Slicer 预设解析校验（编辑器菜单，无需 Play 模式）。
    /// 验收基准来自 folder_viewer/体渲染验证记录.json 的 info.count=32（31 内置 + 1 快照），
    /// MIP 预设按 web 的 -MIP 后缀约定应为 2 个（CT-MIP、MR-MIP）。
    /// </summary>
    public static class SlicerPresetValidationMenu
    {
        private const int ExpectedMipPresetCount = 2;

        [MenuItem("Tools/Volume Rendering/校验 Slicer 预设解析 (vp.json)")]
        public static void ValidateSlicerPresets()
        {
            if (!SlicerPresetLibrary.LoadPresets(forceReload: true))
            {
                Debug.LogError("[SlicerPresetValidation] 预设目录加载失败");
                return;
            }

            var presets = SlicerPresetLibrary.Presets;
            foreach (SlicerPreset preset in presets)
            {
                float colourMin = preset.colourPoints.Min(p => p.hu);
                float colourMax = preset.colourPoints.Max(p => p.hu);
                float alphaMin = preset.opacityPoints.Min(p => p.hu);
                float alphaMax = preset.opacityPoints.Max(p => p.hu);
                Debug.Log($"[SlicerPresetValidation] {preset.name}：颜色 {preset.colourPoints.Count} 点 [{colourMin:0.##}, {colourMax:0.##}]，" +
                          $"alpha {preset.opacityPoints.Count} 点 [{alphaMin:0.##}, {alphaMax:0.##}]，" +
                          $"插值 {preset.interpolationType}，{(preset.IsMip ? "MIP" : "合成")}");
            }

            int mipCount = presets.Count(p => p.IsMip);
            bool shapeOk = presets.All(p => p.colourPoints.Count >= 2 && p.opacityPoints.Count >= 2);
            bool pass = presets.Count == SlicerPresetLibrary.ExpectedPresetCount
                        && mipCount == ExpectedMipPresetCount
                        && shapeOk;

            Debug.Log($"[SlicerPresetValidation] {(pass ? "PASS" : "FAIL")}：数量 {presets.Count}/{SlicerPresetLibrary.ExpectedPresetCount}（期望 31 内置 + 1 快照），" +
                      $"MIP 预设 {mipCount}/{ExpectedMipPresetCount}，控制点形状校验 {(shapeOk ? "通过" : "失败")}。" +
                      (pass ? "" : " 注意：校验的是解析层；视觉映射需进 Play 模式切换预设人工确认。"));
        }
    }
}
