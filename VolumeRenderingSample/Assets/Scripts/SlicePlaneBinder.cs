using UnityVolumeRendering;
using UnityEngine;

namespace VolumeRenderingSample
{
    /// <summary>
    /// 把体数据的切片影像渲染到场景中任意 Plane（Unity 默认 Plane：10×10、UV 0..1）。
    /// 复用包的 SliceRenderingShader，但矩阵由本组件构造——不能直接用 Plane 的 localToWorld：
    /// 该 shader 把 UV 重映射为平面局部 ±5 单位，而体数据只占父级空间 ±0.5
    /// （包内置 SlicingPlane 预制体正是靠 localScale=0.1 缩到正确范围），默认 Plane 缩放为 1 会整体越界。
    ///
    /// 两种模式：
    /// - 投屏模式（screenMode=true，默认）：整个 Plane 显示一整张切片，Plane 可放任意位置、任意大小；
    ///   切片层由 slicePosition（0..1，沿 Plane 法向在体数据内的深度）控制，与 Plane 的世界位置解耦。
    /// - 真实剖切模式（screenMode=false）：切片层跟随 Plane 的实际位置（Plane 必须与体数据相交），
    ///   影像按真实米制尺寸显示，相交区域外为黑色。
    ///
    /// 每帧推送 _parentInverseMat/_planeMat 并同步数据/传递函数纹理，
    /// 切换数据集、重置传递函数后无需重新绑定。
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public class SlicePlaneBinder : MonoBehaviour
    {
        /// <summary>要显示切片的体对象；为空时自动取当前激活数据集。</summary>
        public VolumeRenderedObject targetObject;

        [Tooltip("投屏模式：整个 Plane 显示完整切片（任意位置/大小）；关闭=真实剖切（仅相交处显示）")]
        public bool screenMode = true;

        [Range(0f, 1f)]
        [Tooltip("投屏模式下的切片层位置（沿 Plane 法向，0=一侧边缘，0.5=正中，1=另一侧边缘）；仅当未跟随 SlicingPlane 时生效")]
        public float slicePosition = 0.5f;

        [Tooltip("跟随场景中的包内置切面平面（SlicingPlane）：它切到哪一层/哪个朝向，本 Plane 就镜像显示哪一层。无 SlicingPlane 时回退到 slicePosition")]
        public bool followSlicingPlane = true;

        /// <summary>当前是否正在跟随场景中的 SlicingPlane（供 UI 决定是否隐藏切片层滑条）。</summary>
        public bool IsFollowingSlicingPlane => followSlicingPlane && followedSlice != null;

        // 包 shader 把 UV(0..1) 映射为局部 ±5；体数据局部范围是 ±0.5，故固定缩放 0.1
        private const float ShaderUvToLocalScale = 0.1f;

        private Material sliceMaterial;
        private SlicingPlane followedSlice;
        private float nextSliceScanTime;

        private void Start()
        {
            EnsureSetup();
        }

        private void EnsureSetup()
        {
            if (sliceMaterial != null && targetObject != null)
                return;

            if (targetObject == null && RuntimeVolumeLoader.Instance != null)
                targetObject = RuntimeVolumeLoader.Instance.ActiveObject;
            if (targetObject == null || targetObject.dataset == null)
                return;

            MeshRenderer renderer = GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                Debug.LogError("[SlicePlaneBinder] 需要 MeshRenderer 组件");
                enabled = false;
                return;
            }

            sliceMaterial = new Material(ShaderFactory.GetSliceRenderingShader());
            renderer.material = sliceMaterial;
        }

        private void Update()
        {
            if (sliceMaterial == null || targetObject == null || targetObject.dataset == null)
            {
                EnsureSetup();
                if (sliceMaterial == null || targetObject == null || targetObject.dataset == null)
                    return;
            }

            Transform container = targetObject.volumeContainerObject.transform;

            // 每帧同步纹理：切换数据集/重置传递函数后自动跟随（两个 Get 均返回缓存实例，开销可忽略）
            sliceMaterial.SetTexture("_DataTex", targetObject.dataset.GetDataTexture());
            sliceMaterial.SetTexture("_TFTex", targetObject.transferFunction != null
                ? targetObject.transferFunction.GetTexture()
                : Texture2D.whiteTexture);

            sliceMaterial.SetMatrix("_parentInverseMat", container.worldToLocalMatrix);

            // 跟随模式：镜像场景中 SlicingPlane 的切层与朝向（定时扫描它的创建/销毁）
            if (followSlicingPlane)
            {
                if (followedSlice == null && Time.unscaledTime >= nextSliceScanTime)
                {
                    nextSliceScanTime = Time.unscaledTime + 0.5f;
#if UNITY_2023_1_OR_NEWER
                    followedSlice = UnityEngine.Object.FindFirstObjectByType<SlicingPlane>();
#else
                    followedSlice = UnityEngine.Object.FindObjectOfType<SlicingPlane>();
#endif
                }
                if (followedSlice != null)
                {
                    sliceMaterial.SetMatrix("_planeMat", ComputeMirrorMatrix(container, followedSlice.transform));
                    return;
                }
            }

            sliceMaterial.SetMatrix("_planeMat", screenMode
                ? ComputeScreenPlaneMatrix(container)
                : ComputeCutoutPlaneMatrix());
        }

        /// <summary>
        /// 跟随 SlicingPlane：切层与朝向完全取自它（局部 XZ 为切面、Y 为法向，与包 shader 约定一致），
        /// 本 Plane 自身的 Transform 不参与成像，只决定显示屏摆在哪里。
        /// </summary>
        private Matrix4x4 ComputeMirrorMatrix(Transform container, Transform slice)
        {
            Vector3 normal = container.InverseTransformDirection(slice.up).normalized;
            Vector3 axisU = container.InverseTransformDirection(slice.right).normalized;
            Vector3 axisV = container.InverseTransformDirection(slice.forward).normalized;
            Vector3 sliceLocalPos = container.InverseTransformPoint(slice.position);
            float sliceOffset = Vector3.Dot(sliceLocalPos, normal);

            Matrix4x4 basis = Matrix4x4.identity;
            basis.SetColumn(0, axisU * ShaderUvToLocalScale);
            basis.SetColumn(1, Vector4.zero); // shader 输入 y=0，该列不影响结果
            basis.SetColumn(2, axisV * ShaderUvToLocalScale);
            basis.SetColumn(3, new Vector4(normal.x * sliceOffset, normal.y * sliceOffset, normal.z * sliceOffset, 1f));
            return container.localToWorldMatrix * basis;
        }

        /// <summary>
        /// 投屏模式：在体数据局部空间构造虚拟切片四边形，覆盖数据全范围（±0.5）。
        /// shader 端顶点公式 mul(_planeMat, (uvMod.x, 0, uvMod.y, 1))：列 0/列 2 是平面两个切向轴
        /// （对应局部 X/Z），平移列是切片层偏移（沿法向）。位置取自 slider，与世界位置无关。
        /// </summary>
        private Matrix4x4 ComputeScreenPlaneMatrix(Transform container)
        {
            Vector3 normal = container.InverseTransformDirection(transform.up).normalized;
            Vector3 axisU = container.InverseTransformDirection(transform.right).normalized;
            Vector3 axisV = container.InverseTransformDirection(transform.forward).normalized;
            float sliceOffset = slicePosition - 0.5f;

            Matrix4x4 basis = Matrix4x4.identity;
            basis.SetColumn(0, axisU * ShaderUvToLocalScale);
            basis.SetColumn(1, Vector4.zero); // shader 输入 y=0，该列不影响结果
            basis.SetColumn(2, axisV * ShaderUvToLocalScale);
            basis.SetColumn(3, new Vector4(normal.x * sliceOffset, normal.y * sliceOffset, normal.z * sliceOffset, 1f));
            return container.localToWorldMatrix * basis;
        }

        /// <summary>
        /// 真实剖切模式：与包内置 SlicingPlane 一致——切片位于 Plane 的世界位置处（局部 XZ 平面，
        /// 法向为局部 Y），但缩放强制为 0.1（shader 把 UV 映射为局部 ±5，需缩到体数据的 ±0.5）。
        /// </summary>
        private Matrix4x4 ComputeCutoutPlaneMatrix()
        {
            return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one * ShaderUvToLocalScale);
        }
    }
}
