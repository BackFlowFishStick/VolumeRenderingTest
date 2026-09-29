using UnityEditor;
using UnityEngine;

namespace VolumeRenderingSample.EditorTools
{
    /// <summary>
    /// 在编辑模式下把 SlicePlaneBinder 永久添加到选中的 Plane 上。
    /// 编辑模式添加的组件随场景保存，每次 Play 自动绑定当前激活的体数据集——
    /// 相比运行时点按钮绑定（组件不跨会话保留），这是推荐方式。
    /// </summary>
    public static class SliceBinderMenu
    {
        [MenuItem("Tools/Volume Rendering/Add Slice Binder To Selected Plane")]
        public static void AddSliceBinderToSelection()
        {
            GameObject go = Selection.activeGameObject;
            if (go == null || go.GetComponent<MeshRenderer>() == null)
            {
                Debug.LogError("[SliceBinderMenu] 请先在 Hierarchy 中选中带 MeshRenderer 的对象（例如你的 Plane）");
                return;
            }

            if (go.GetComponent<SlicePlaneBinder>() == null)
                Undo.AddComponent<SlicePlaneBinder>(go);

            Debug.Log($"[SliceBinderMenu] 已为 {go.name} 添加 SlicePlaneBinder。运行时会自动绑定当前激活的体数据集；screenMode=true 时用面板上的「切片层」滑条翻层。");
        }
    }
}
