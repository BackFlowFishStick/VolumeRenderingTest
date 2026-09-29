using System.IO;
using UnityEngine;

namespace VolumeRenderingSample
{
    /// <summary>
    /// 数据路径解析：StreamingAssets 优先（将来打包可直接用），编辑器下回退到 Assets/Resources。
    /// .dcm/.nrrd 是 Unity 不识别的资产，无法用 Resources.Load，只能文件 IO 直读。
    /// </summary>
    public static class DatasetPathResolver
    {
        public static string ResolveDirectory(string folderName)
        {
            string[] candidates =
            {
                Path.Combine(Application.streamingAssetsPath, folderName),
#if UNITY_EDITOR
                Path.Combine(Application.dataPath, "Resources", folderName),
#endif
            };

            foreach (string path in candidates)
            {
                if (Directory.Exists(path))
                    return path;
            }

            Debug.LogError($"[DatasetPathResolver] 未找到目录 {folderName}，已尝试：\n{string.Join("\n", candidates)}");
            return null;
        }

        public static string ResolveFile(string fileName)
        {
            string[] candidates =
            {
                Path.Combine(Application.streamingAssetsPath, "Datasets", fileName),
                Path.Combine(Application.streamingAssetsPath, fileName),
#if UNITY_EDITOR
                Path.Combine(Application.dataPath, "Resources", fileName),
#endif
            };

            foreach (string path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            Debug.LogError($"[DatasetPathResolver] 未找到文件 {fileName}，已尝试：\n{string.Join("\n", candidates)}");
            return null;
        }
    }
}
