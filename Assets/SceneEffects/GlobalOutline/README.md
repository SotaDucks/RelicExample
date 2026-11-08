# 逐物体外轮廓描边

打开 Unity 菜单 **Tools → Rendering → 全局描边设置**，或选择 `Assets/Resources/GlobalOutlineSettings.asset`。

- **描边粗细（像素）**：统一调整。0 关闭；正数按原始渲染像素向上取整，放大前最小为 1 像素。
- **暗边颜色、轮廓暗边强度、外沿微弱高光**：分别控制颜色、暗边混合和邻近外沿的微弱高光。
- **自动覆盖的物体**：树冠、灌木丛、树干为三个独立开关；另有建筑与灯笼、蜡烛、石头与岩壁、傩戏面具开关。同一个树模型的树叶和树干子材质也可以分别控制。
- **傩戏面具**：自动识别层级名称含“面具”“傩面”或“Mask”的模型及其子物体，复制后的面具也会自动接入。整件面具使用共同轮廓身份，可单独启用或关闭。
- **高级 → 细缝修补**：建议 0～1，尽量贴合物体。值较大时轮廓会简化。
- **高级 → 忽略树冠内部碎孔**：过滤叶片透明贴图在树冠内部造成的碎线，保留外缘。
- 修改后点击 **保存描边设置**。

每个物体拥有独立身份，相邻物体不会合并成一张轮廓。树干与树冠、亭子的屋顶与柱子等同一模型的部件共同组成一个外轮廓。描边跟随可见剪影与遮挡边界，不通过内部纹理、面片法线或光照变化生成线条。被遮挡的部分不会穿过前景显示。火苗、萤火虫、光束、角色、地砖与草叶不参与新增描边。

已匹配本项目 SG_Plant 树叶材质的风动、UV1 权重和灰度/Alpha 裁切规则，使轮廓跟随树冠摆动。

自动归组依据模型层级与 LODGroup。如某个复合模型的部件未正确归组，在该模型根物体添加 **GlobalOutlineObject** 组件即可指定共同轮廓身份。请不要把此组件加在整个场景或整片森林的根节点。

描边在像素画面放大前执行。原来重复叠加的 `OutLine` RenderObjects 通道已停用；所选物体原材质的深度/法线描边通过临时 MaterialPropertyBlock 抑制，关闭本系统或取消选择时恢复。未改写摄影机、地形、模型 Transform、GameObject Layer 或原材质资源。

程序保留 Rendering Layer 的第 30 位作为描边筛选位，其余位保持不变，退出加载状态时恢复。运行时和编辑器均自动更新；新增 MeshRenderer 通常在 0.8 秒内加入。GPU 间接绘制、无 MeshRenderer 的自定义植被需要单独接入。

运行时代码示例：

```csharp
using RelicExample.Rendering;

GlobalOutlineSettings.Instance.SetThickness(1f);
GlobalOutlineSettings.Instance.SetColor(UnityEngine.Color.black);
GlobalOutlineSettings.Instance.SetEnabled(true);
```

代码位于 `Assets/SceneEffects/GlobalOutline/`，已接入当前 URP 管线的两个 Renderer。使用 Unity 6 / URP 17.3 Render Graph，不依赖缺失的第三方 ProPixelizer。

视觉方向参考：https://sotaducks.github.io/TestYaoguaiEnvironment/
