# 更新日志

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/lang/zh-CN/).

## [v1.0.0] - 2026-02-22

### ✨ 新特性 (Features)
*   **快捷键截图**
    > 全局热键触发截图，默认 `Ctrl+Shift+A`，支持自定义修改。

*   **屏幕框选**
    > 蓝色框线选区 UI，实时显示像素尺寸。

*   **自动保存与路径复制**
    > 截图后自动保存到 Pictures\PathSnip 目录，路径自动复制到剪贴板，终端中直接 Ctrl+V 粘贴。

*   **托盘运行**
    > 最小化到系统托盘，后台常驻，支持托盘菜单操作。

*   **设置窗口**
    > 支持自定义快捷键、保存目录、开机自启、通知开关。

*   **GitHub Actions CI/CD**
    > 每次 push 自动构建验证。

### 🐛 问题修复 (Bug Fixes)
*   **蓝色蒙版问题**: 修复截图时选区窗口被截入的问题，现在截图清晰无蒙版。

### 🚀 优化 (Optimizations)
*   **轻量体积**: 编译产物 < 1MB，零外部依赖（仅依赖 .NET Framework 4.8 系统预装）。
