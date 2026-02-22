<div align="center">
  <img src="src/PathSnip/Resources/PathSnip.ico" width="128" height="128" alt="PathSnip Logo">
  <h1>PathSnip</h1>
  <p>轻量级截图工具，专为开发者设计</p>
  
  <p>
    <img src="https://img.shields.io/badge/.NET-Framework_4.8-blue" alt=".NET Framework 4.8">
    <img src="https://img.shields.io/badge/platform-Windows_10%2F11-blue" alt="Windows 10/11">
    <img src="https://img.shields.io/badge/license-MIT-green" alt="License MIT">
  </p>
</div>

---

## 特性

- ⚡ **快捷截图** - 全局热键触发，支持自定义
- 📋 **路径直通车** - 截图后路径自动复制到剪贴板
- 🎨 **蓝色框选** - 简洁大气的选区 UI
- 📦 **轻量体积** - 编译产物 < 1MB，零外部依赖
- 🔔 **托盘运行** - 后台常驻，不打扰工作

## 快速开始

### 下载

从 [Releases](https://github.com/mcheiyue/PathSnip/releases) 下载最新版本。

### 使用方法

1. 运行 `PathSnip.exe`
2. 程序自动最小化到托盘
3. 按 `Ctrl+Shift+A` 截图
4. 框选区域 → 自动保存 → 路径已复制
5. 终端中 `Ctrl+V` 粘贴路径

## 快捷操作

| 操作 | 说明 |
|------|------|
| `Ctrl+Shift+A` | 截图（可自定义） |
| 左键拖拽 | 框选区域 |
| 右键 / ESC | 取消截图 |
| 托盘左键 | 截图 |
| 托盘右键 | 菜单 |

## 系统要求

- Windows 10 (1809+) / Windows 11
- .NET Framework 4.8（系统预装，无需安装）

## 技术栈

- .NET Framework 4.8
- WPF
- Hardcodet.NotifyIcon.Wpf

## 目录结构

```
PathSnip/
├── src/PathSnip/       # 源代码
│   ├── Services/       # 核心服务
│   └── Resources/      # 资源文件
├── .github/workflows/  # CI/CD
├── CHANGELOG.md        # 变更日志
└── README.md           # 说明文档
```

## 许可证

MIT License - 请查看 [LICENSE](LICENSE) 文件。

---

<div align="center">
  <sub>Made with ❤️ by <a href="https://github.com/mcheiyue">mcheiyue</a></sub>
</div>
