# RelicExample 项目检查说明

本次提交整理了当前已保存的场景、地形、水体、树木和植被材质，以及相关预制体修改，供课程检查。

## 打开工程

- Unity 编辑器版本：**6000.3.7f1**，以 `ProjectSettings/ProjectVersion.txt` 为准。
- 渲染管线：**URP 17.3.0**。
- 主要检查场景：**`Assets/场景/ZQY.unity`**。

请先安装 Git LFS，再克隆工程并下载大文件：

```sh
git lfs install
git clone https://github.com/cimo7691-alt/RelicExample.git
cd RelicExample
git lfs pull
```

在 Unity Hub 中添加工程根目录，使用上述版本打开，等待资源导入和包解析完成，再打开主要检查场景。

## 本次检查内容

- `Assets/场景/ZQY.unity`：当前主要场景布置。
- `Assets/场景的地形数据.asset`：地形数据更新。
- `Assets/各种材质/`：水体、树叶、树干、石头等材质与相关预制体。
- 资源包目录中随场景使用的材质、预制体及场景调整。

工程源文件包含 `Assets`、`Packages` 和 `ProjectSettings`。`Library`、`Temp`、`Logs` 等本地生成目录由 Unity 在导入时生成。

本次提交以磁盘上已保存的工程为准，未执行新的打包或运行测试。