<div align="center">
  <img src="src/PathSnip/Resources/PathSnip.ico" width="128" height="128" alt="PathSnip Logo">
  <h1>PathSnip</h1>
  <p>面向开发者的截图与路径管理工具</p>
  <p><a href="README.md">简体中文</a> | <a href="README_EN.md">English</a></p>
  <p>
    <img src="https://img.shields.io/badge/version-v1.2.0-brightgreen" alt="Version v1.2.0">
    <img src="https://img.shields.io/badge/.NET-Framework_4.8-blue" alt=".NET Framework 4.8">
    <img src="https://img.shields.io/badge/platform-Windows_10%2F11-blue" alt="Windows 10/11">
    <img src="https://img.shields.io/badge/license-GPL--3.0-blue" alt="License GPL-3.0">
  </p>
</div>

---

## 项目定位

PathSnip 是一款桌面端截图工具，强调 **智能吸附、放大镜与拾色器、贴图** 和 **路径/剪贴板管理**。适合开发者在截图、标注和路径复制场景中快速完成工作流。

## 核心特性

- **智能吸附引擎**：支持多模式（Auto/WindowOnly/ElementPreferred/ManualOnly），可按住 Alt 临时绕过吸附。
- **放大镜与拾色器**：截图时提供像素级放大，`C` 快捷键复制色值。
- **贴图（Pinned Image）**：截图后可贴图悬浮对照，支持拖拽、滚轮缩放、透明度调节与双击关闭。
- **选区交互**：锁定选区后未选择标注工具时，可在选区内部拖动平移（仅改变裁剪区域，不移动已画标注/马赛克）。
- **剪贴板模式**：支持 PathOnly/ImageOnly/ImageAndPath，并可设置路径格式。
- **文件命名模板**：支持多种时间与 GUID 占位符，方便统一命名。
- **托盘常驻**：全局热键触发、托盘菜单管理，支持打开设置/目录与退出。

## 快速开始

1. 从 [Releases](https://github.com/mcheiyue/PathSnip/releases) 下载最新版本。
2. 运行 `PathSnip.exe`，程序将常驻系统托盘。
3. 按 `Ctrl+Shift+A` 触发截图，框选区域。
4. 截图自动保存到 `Pictures\PathSnip`，并按配置写入剪贴板。
5. 可在设置中调整热键、保存目录、剪贴板模式与智能吸附。

> 构建与运行依赖：.NET Framework 4.8（运行时）/ .NET SDK 8.0（构建）。

## 快捷键与交互

| 操作 | 说明 |
|------|------|
| `Ctrl+Shift+A` | 全局截图热键（可在设置修改） |
| `Esc` | 取消截图 |
| `Enter` | Overlay：确认保存并退出（文字输入框聚焦时不抢占） |
| `Ctrl+Z` | Overlay：撤销上一笔标注（文字输入框聚焦时不抢占） |
| `Tab` / `Shift+Tab` | 循环切换候选/控件 |
| `T` | 贴图（Pinned Image） |
| `C` | 复制当前色值 |
| `Alt` | 按住绕过智能吸附 |

## 功能详解

### 智能吸附
- 模式：`Auto` / `WindowOnly` / `ElementPreferred` / `ManualOnly`
- 开关：`EnableSmartSnap`、`EnableElementSnap`
- 绕过：`HoldAltToBypassSnap`（按住 Alt 临时绕过）

### 放大镜与拾色器
- 截图过程中提供像素级放大定位。
- 按 `C` 可复制当前像素色值到剪贴板。

### 贴图（Pinned Image）
- 在截图完成后按 `T` 生成贴图窗口。
- 支持拖拽移动、滚轮缩放、透明度调节、双击关闭。

### 剪贴板与路径格式
- 剪贴板模式：`PathOnly` / `ImageOnly` / `ImageAndPath`
- 路径格式：`Text` / `Markdown` / `HTML`
  - Markdown 示例：`![截图](C:\\Path\\to\\image.png)`
  - HTML 示例：`<img src="file:///C:/Path/to/image.png"/>`

### 文件命名模板
- 默认模板：`{yyyy}-{MM}-{dd}_{HHmmss}`
- 支持占位符：`{yyyy}{MM}{dd}{HH}{mm}{ss}{HHmmss}{GUID}`（GUID 取 8 位）
- 文件保存扩展名：`.png`

## 设置与配置

- 配置文件路径：`%APPDATA%\PathSnip\config.json`
- 默认保存目录：`Pictures\PathSnip`
- 默认热键：`Ctrl+Shift+A`
- 智能吸附：支持模式与元素吸附开关
- 其它选项：剪贴板模式、路径格式、文件名模板、开机自启、通知

> **重置默认提醒**：设置页的“重置默认”会将保存目录重置为 *图片库根目录*，与默认配置 `Pictures\PathSnip` 不同。升级或迁移时请确认该差异。

## 路径与日志

- 配置路径：`%APPDATA%\PathSnip\config.json`
- 日志路径：`%APPDATA%\PathSnip\logs\`（保留 7 天）

## 构建与发布

- 本地构建命令：
  ```bash
  dotnet build "d:\github\PathSnip\src\PathSnip\PathSnip.csproj" -c Release
  ```
- 发布说明：打 tag 触发 `release.yml`，产物位于 `bin/Release/net48/PathSnip.exe`。
- Release Notes 自动从 `CHANGELOG.md` 提取。

## 变更摘要（v1.0.0 → v1.2.0）

- **v1.2.0**：Overlay 纯键盘流补齐（Enter 保存、Ctrl+Z 撤销），并修复文字标注结束后焦点丢失导致的快捷键失效。
- **v1.1.9**：无工具状态支持拖动平移选区；聚焦高频使用下的稳定性修复与性能收敛。
- **v1.1.8**：快动场景吸附/探测节流与稳定性修正；补齐构建清单与文档入口。
- **v1.1.7**：区域级智能吸附引擎（UIA/MSAA/区域画像/稳定器/模式门控/快动回退）。
- **v1.1.3**：贴图功能（T 键、PinnedImageWindow、拖拽/缩放/透明度）。放大镜渲染与缓存优化。
- **v1.1.2**：像素级放大镜 + 拾色器（C 键复制）。
- **v1.1.1**：智能窗口吸附能力。
- **v1.0.x**：标注工具体系、剪贴板三模式、文件名模板与高 DPI 修复等。

## 迁移与提示

- 升级前建议备份 `%APPDATA%\PathSnip\config.json`。
- 检查智能吸附模式与 `HoldAltToBypassSnap` 是否符合你的操作习惯。
- 若使用“重置默认”，保存目录会回到图片库根目录，请提前确认。

## 许可证

本项目使用 **GPL-3.0** 许可证，详见 [LICENSE](LICENSE)。

---

<div align="center">
  <sub>Made with ❤️ by <a href="https://github.com/mcheiyue">mcheiyue</a></sub>
</div>
