// UI 切片查看器专用着色器：按 UV 直接采样体数据某一切层并套用传递函数。
// 采样逻辑（_DataTex 原始值 → _TFTex）与包的 SliceRenderingShader 保持一致，确保与体渲染外观相同。
Shader "Hidden/UVR_SliceUI"
{
    Properties
    {
        _DataTex("Data Texture (3D)", 3D) = "" {}
        _TFTex("Transfer Function Texture", 2D) = "white" {}
        _SlicePos("Slice Position", Range(0,1)) = 0.5
        _Axis("Slice Axis (0=X,1=Y,2=Z)", Float) = 2
        _SecondaryDataTex("Segmentation Data (3D)", 3D) = "" {}
        _SecondaryTFTex("Segmentation TF Texture", 2D) = "white" {}
        _UseSegmentation("Use Segmentation Overlay", Float) = 0
    }
    SubShader
    {
        PackageRequirements { "com.unity.render-pipelines.universal" }
        Tags { "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            Texture3D _DataTex;             SamplerState sampler_DataTex;
            Texture2D _TFTex;               SamplerState sampler_TFTex;
            Texture3D _SecondaryDataTex;    SamplerState sampler_SecondaryDataTex;
            Texture2D _SecondaryTFTex;      SamplerState sampler_SecondaryTFTex;
            float _SlicePos;
            float _Axis;
            float _UseSegmentation;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = TransformObjectToHClip(v.vertex.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                float3 dataCoord;
                if (_Axis < 0.5)        dataCoord = float3(_SlicePos, i.uv.x, i.uv.y);  // 沿 X：矢状向
                else if (_Axis < 1.5)   dataCoord = float3(i.uv.x, _SlicePos, i.uv.y);  // 沿 Y：纵向（冠状向）
                else                    dataCoord = float3(i.uv.x, i.uv.y, _SlicePos);  // 沿 Z：横向（轴状向）

                float dataVal = _DataTex.Sample(sampler_DataTex, dataCoord);
                half4 col = _TFTex.Sample(sampler_TFTex, float2(dataVal, 0.0));

                // 分割叠加：该体素属于某个结构（第二传递函数 alpha>0）时，用结构色替换组织色。
                // 与包 DVR 的逻辑一致（src = secondaryColour.a > 0 ? secondaryColour : src）。
                if (_UseSegmentation > 0.5)
                {
                    float segVal = _SecondaryDataTex.Sample(sampler_SecondaryDataTex, dataCoord).r;
                    half4 segCol = _SecondaryTFTex.Sample(sampler_SecondaryTFTex, float2(segVal, 0.0));
                    if (segCol.a > 0.0)
                        col = segCol;
                }

                col.a = 1.0f;
                return col;
            }
            ENDHLSL
        }
    }
}
