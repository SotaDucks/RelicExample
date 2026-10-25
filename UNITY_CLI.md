# Unity CLI 配置

本项目使用官方 Unity CLI，编辑器版本由 ProjectSettings/ProjectVersion.txt 固定为 6000.3.7f1。配置时检测到本机 CLI 版本为 1.0.0-beta.13，命令名为 unity，已加入本机 PATH。

Packages/manifest.json 已将 com.unity.pipeline 固定为 0.9.1-exp.1。Unity Package Manager 会在编辑器刷新项目时下载该包并自动解析其传递依赖，包括测试框架、Mono.Cecil、Newtonsoft.Json 和相关 Unity 内置模块。packages-lock.json 应由 Unity 自动生成，不要手动伪造解析结果。

ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json 已启用编辑器服务自动启动和每五秒一次的自恢复检查。端口自动从包提供的范围选择，默认范围为 7800–7849。连接限定本机回环地址；CLI 自动读取 Library 内的连接信息，无需提交令牌或手动配置固定端口。

项目已有 URP 17.3.0、Shader Graph 和 Core 渲染依赖。这里的渲染管线与 CLI 所需的 Unity Pipeline 是不同的软件包。

## 使用与验证

在项目根目录打开 PowerShell，使用 unity 命令即可访问官方 CLI。查看连接状态使用 status 子命令；检查包及服务状态使用 pipeline list；读取当前编辑器状态使用 command 下的 editor status 命令。存在多个项目时，使用 project-path 参数指定本项目的绝对路径。

首次配置后，切换到 Unity 编辑器并等待包下载、导入与编译结束。如果正在运行游戏，先停止运行以便 Unity 完成包更新。连接状态应显示当前项目及 ready。

如果包已导入但服务仍未启动，在 Unity 的 Window 菜单下进入 Pipeline，再选择 Start Server。也可以在 Project Settings 的 Pipeline / Editor 页面检查服务配置。若出现 Safe Mode，需先处理 Console 中的编译错误。

本次配置未编辑场景，不需要在场景中挂载任何组件。不要关闭带有未保存修改的编辑器来强制重新导入。

## 官方资料

- [Unity CLI 使用说明](https://docs.unity.com/en-us/unity-cli/use-unity-cli)
