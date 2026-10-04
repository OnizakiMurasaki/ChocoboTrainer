# Chocobo Trainer

用于《最终幻想 XIV》竞赛陆行鸟训练流程的 Dalamud 插件。

目前主要在国服 / XIVLauncherCN 环境下开发与测试。

## 功能

- 在驯鸟师附近自动开始竞赛陆行鸟训练。
- 可在设置中选择训练饲料。
- 支持单次训练和循环训练。
- 支持后台运行，不通过鼠标或键盘模拟完成操作。
- 不依赖 YesAlready。
- 可设置各属性停止值：当训练后的预览值达到或超过设定值时，在点击“开始训练”前停止。
- 提供保守的属性溢出保护。
- 无法找到驯鸟师、无法触发训练流程或满足停止条件时，会结束自动训练并在插件窗口显示状态。

## 使用方法

在竞赛陆行鸟驯鸟师附近打开插件窗口，选择饲料后点击“开始训练”。

插件命令：

```text
/ctrain
/ctrain start
/ctrain once
/ctrain stop
/ctrain config
/ctrain settings
/ctrain status
```

其中：

- `/ctrain` 或 `/ctrain start`：开始循环训练。
- `/ctrain once`：执行一次训练。
- `/ctrain stop`：结束训练。
- `/ctrain config` 或 `/ctrain settings`：打开设置。
- `/ctrain status`：打开主窗口。

## 构建

项目使用 Dalamud API 15。

在本项目的国服开发环境中，可使用：

```powershell
$env:DALAMUD_HOME = "$env:APPDATA\XIVLauncherCN\addon\Hooks\dev"
dotnet build ".\ChocoboTrainer\ChocoboTrainer.csproj" -c Release
```

## 安装

本项目计划通过自定义插件仓库发布，不提交至官方 Dalamud 插件仓库。

首次正式 Release 发布后，可通过本仓库提供的 `pluginmaster-cn.json` 添加到 XIVLauncherCN 的自定义插件仓库中。

## AI 辅助开发说明

本项目在开发过程中使用了 OpenAI ChatGPT 进行辅助。

AI 主要用于代码实现、API 调研、调试及文档整理；插件功能设计、需求制定、游戏内 UI / 事件分析、实际测试和结果验证均由维护者完成。

所有发布版本均由维护者实际编译并进行游戏内测试。

## 免责声明

这是第三方插件，与 Square Enix、Dalamud、XIVLauncherCN 官方无关联。请自行评估并承担使用第三方插件可能带来的风险。

## License

见 [LICENSE.md](LICENSE.md)。
