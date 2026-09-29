# VolumeRenderingTest

体渲染（Volume Rendering）Unity 工程与文档仓库。

## 仓库结构

| 目录 | 说明 |
| --- | --- |
| `VolumeRenderingSample/` | **Unity 6 (6000.0)** 示例工程 |
| `VolumeRendering_2022/` | **Unity 2022.3** 示例工程 |
| `UnityVolumeRendering/` | 体渲染 Package（Easy Volume Renderer，基于 [mlavik1/UnityVolumeRendering](https://github.com/mlavik1/UnityVolumeRendering)） |
| `LocalDoc/` | 项目整理文档（含 `体渲染知识/` 专题） |

## 使用说明

- **Unity 6 工程**：用 Unity 6000.0 打开 `VolumeRenderingSample/`。
- **Unity 2022.3 工程**：用 Unity 2022.3 打开 `VolumeRendering_2022/`。
- **体渲染 Package**：`UnityVolumeRendering/` 为 UPM 包目录，可作为本地包
  （`file:.../UnityVolumeRendering`）引用，或直接拷贝进工程 `Assets/` 使用。
- **文档**：见 [`LocalDoc/README.md`](LocalDoc/README.md)。

## 版本控制说明

- 仓库遵循 Unity 工程版本控制标准：`Library/`、`Temp/`、`Obj/`、`Logs/`、
  `UserSettings/` 等生成目录与 IDE 文件不入库，打开工程后由 Unity 自动生成。
- `Assets/StreamingAssets/` 下的医学示例数据（DICOM/NRRD/MRB）随工程入库，
  用于示例场景直接运行。
- `UnityVolumeRendering/ThirdParty/SimpleITK/SimpleITKCSharpNative.dll` 体积
  超过 GitHub 单文件 100MB 限制，通过 **Git LFS** 管理，克隆时需安装
  [Git LFS](https://git-lfs.com/)。

## 许可

- 体渲染 Package：MIT（详见 `UnityVolumeRendering/LICENSE.md`）。
- 文档与示例工程：见各目录内说明。
