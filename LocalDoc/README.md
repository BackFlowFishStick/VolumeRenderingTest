# UnityVolumeRendering 项目文档（LocalDoc）

> 本文档由对仓库 `UnityVolumeRendering/` 的代码与文档进行整理后生成，用于快速理解该项目的**目的、功能、架构与使用方式**。
>
> 仓库根路径：`C:\UnityProjs\TestVolumeRendering\UnityVolumeRendering`

## 这是什么项目？

**UnityVolumeRendering**（包名 `com.mlavik1.easyvolumerenderer`，显示名 **Easy Volume Renderer**）
是一个基于 **Unity3D** 的**医学/科学体数据（volume data）可视化与渲染插件**。
它把 DICOM、NIFTI、NRRD、VASP/PARCHG、RAW 等格式的 3D 体数据集导入 Unity，
通过 **GPU 光线步进（raymarching）** 在片元着色器中实时渲染。

- 作者：Matias Lavik（[mlavik1](https://github.com/mlavik1)）
- 许可证：MIT（免费，可用于商业项目，仅需保留版权声明）
- 目标 Unity 版本：Unity 6 (6000.0) 及以上（package.json 最低声明 2022.3，CI 同时构建 2022.3 与 6000.0 包）
- 渲染管线支持：Built-in、URP、HDRP

## 文档目录

| 文件 | 内容 |
| --- | --- |
| [01-项目概述与目标.md](01-项目概述与目标.md) | 项目定位、核心目标、主要特性总览 |
| [02-目录结构说明.md](02-目录结构说明.md) | 仓库目录结构与各目录职责 |
| [03-核心功能详解.md](03-核心功能详解.md) | 渲染模式、传输函数、光照、阴影、切面、分割等详细功能 |
| [04-数据导入说明.md](04-数据导入说明.md) | 支持的格式、导入方式、SimpleITK 集成 |
| [05-架构与关键模块.md](05-架构与关键模块.md) | Runtime/Editor/Shader 关键类与模块职责 |
| [06-渲染管线与着色器.md](06-渲染管线与着色器.md) | 光线步进原理、着色器文件、多管线适配 |
| [07-编辑器工具与脚本接口.md](07-编辑器工具与脚本接口.md) | 编辑器菜单/窗口、脚本化 API |
| [08-示例工程与数据.md](08-示例工程与数据.md) | SampleProject~、示例场景、示例数据集 |
| [09-依赖许可与构建发布.md](09-依赖许可与构建发布.md) | 第三方依赖、许可、CI 与版本信息 |
| [10-快速上手指南.md](10-快速上手指南.md) | 安装、导入、渲染、调试的端到端步骤 |
| [11-功能清单.md](11-功能清单.md) | 全部功能按模块汇总：导入/渲染/切面裁剪/工具/平台边界 |

> 📚 **体渲染知识专题**：[体渲染知识/README.md](体渲染知识/README.md)
> —— 想真正理解本项目「体渲染原理」的读者请看这里：光线步进算法、
> 传输函数、三种渲染模式、梯度光照阴影、数据管线、性能调参等 9 篇知识文档。

## 项目本质速览

```
体数据集 (DICOM/NIFTI/NRRD/VASP/RAW/图像序列)
      │  导入 (ImporterFactory + 各 Importer)
      ▼
VolumeDataset (3D 密度数据, ScriptableObject)
      │  生成数据纹理/梯度纹理 (Texture3D)
      ▼
VolumeObjectFactory.CreateObject → VolumeRenderedObject
      │  材质 + 着色器 (DVR/MIP/Isosurface) + 传输函数
      ▼
GPU Raymarching 实时渲染（含光照/阴影/切面/分割/多体叠加等）
```

---
*文档生成日期：2026-09-16*
