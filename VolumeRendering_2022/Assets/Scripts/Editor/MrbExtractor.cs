using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEngine;

namespace VolumeRenderingSample.EditorTools
{
    /// <summary>
    /// 解包 3D Slicer 的 .mrb（Medical Reality Bundle，本质为 zip；本包的导入器不识别 .mrb）。
    /// 包内 Data/input_ct.nrrd 是 CT 主体数据，提取到 Assets/StreamingAssets/Datasets/ 后
    /// 交由 Easy Volume Renderer 的 NRRD 导入器（SimpleITK 后端）加载。
    /// </summary>
    public static class MrbExtractor
    {
        private const string MenuPath = "Tools/Volume Rendering/Extract Chest MRB (mrb → StreamingAssets)";

        [MenuItem(MenuPath)]
        public static void ExtractChestMrb()
        {
            string mrbPath = FindMrbFile();
            if (mrbPath == null)
            {
                Debug.LogError("[MrbExtractor] 未在 Assets/Resources 下找到 .mrb 文件");
                return;
            }

            Extract(mrbPath);
            AssetDatabase.Refresh();
        }

        public static string FindMrbFile()
        {
            // 数据常被放在 StreamingAssets（构建可用）或 Resources（仅编辑器），两处都找
            string[] searchDirs =
            {
                Application.streamingAssetsPath,
                Path.Combine(Application.dataPath, "Resources"),
            };

            foreach (string dir in searchDirs)
            {
                if (!Directory.Exists(dir))
                    continue;
                string[] files = Directory.GetFiles(dir, "*.mrb", SearchOption.TopDirectoryOnly);
                if (files.Length > 0)
                    return files[0];
            }
            return null;
        }

        private static void Extract(string mrbPath)
        {
            string outputDir = Path.Combine(Application.streamingAssetsPath, "Datasets");
            Directory.CreateDirectory(outputDir);

            int extracted = 0;
            using (ZipArchive archive = new ZipArchive(File.OpenRead(mrbPath), ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destName = MapEntryName(entry.FullName);
                    if (destName == null)
                        continue;

                    string destPath = Path.Combine(outputDir, destName);
                    if (File.Exists(destPath) && new FileInfo(destPath).Length == entry.Length)
                    {
                        Debug.Log($"[MrbExtractor] 跳过（已存在且大小一致）: {destName}");
                        continue;
                    }

                    ExtractEntry(entry, destPath);
                    Debug.Log($"[MrbExtractor] {entry.FullName} → {destPath} ({entry.Length / (1024f * 1024f):F1} MB)");
                    extracted++;
                }
            }

            Debug.Log($"[MrbExtractor] 解包完成：{extracted} 个文件 → {outputDir}");
        }

        /// <summary>只提取关心条目，按文件名映射；mrml 场景描述等跳过。</summary>
        private static string MapEntryName(string entryFullName)
        {
            string fileName = Path.GetFileName(entryFullName);
            if (string.IsNullOrEmpty(fileName))
                return null;
            if (fileName.Equals("input_ct.nrrd", System.StringComparison.OrdinalIgnoreCase))
                return "Chest_CT.nrrd";
            if (fileName.EndsWith(".seg.nrrd", System.StringComparison.OrdinalIgnoreCase))
                return "Chest_Seg.nrrd";
            if (fileName.EndsWith(".csv", System.StringComparison.OrdinalIgnoreCase))
                return fileName;
            return null;
        }

        private static void ExtractEntry(ZipArchiveEntry entry, string destPath)
        {
            using (Stream source = entry.Open())
            using (FileStream target = new FileStream(destPath, FileMode.Create, FileAccess.Write))
                source.CopyTo(target);
        }
    }
}
