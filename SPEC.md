# PathSnip 产品规格文档

## 一、产品概述

### 1.1 产品定位
PathSnip 是一款面向开发者的轻量级截图工具，核心价值在于**截图后直接获取文件路径**，实现「截图 → 保存 → 路径进剪贴板」的极速工作流。

### 1.2 目标用户
- 经常在 CLI/TUI 终端环境中工作的开发者
- 需要快速分享截图路径的技术人员

### 1.3 核心特性
| 特性         | 描述                             |
| ------------ | -------------------------------- |
| 快捷截图     | 全局热键 Ctrl+Shift+A 触发      |
| 自动保存     | 截图自动保存到 Pictures\PathSnip |
| 路径入剪贴板 | 保存后自动将绝对路径复制到剪贴板 |
| 托盘运行     | 最小化到系统托盘，后台常驻       |
| 轻量体积     | 目标 < 10MB，零外部依赖          |

---

## 二、技术栈

| 项目         | 说明                              |
| ------------ | --------------------------------- |
| 框架         | .NET Framework 4.8 (Win10/11 预装) |
| UI           | WPF + Hardcodet.NotifyIcon.Wpf    |
| 编译环境     | .NET SDK 8.0                      |
| 目标框架     | net48                             |
| C# 版本      | 7.3                               |
| 依赖         | Hardcodet.NotifyIcon.Wpf 1.1.0    |
|              | Newtonsoft.Json 13.0.3            |

---

## 三、UI/UX 设计

### 3.1 选区框视觉规范
- 边框颜色：#0078D4 (Windows 蓝)
- 边框粗细：2px
- 填充色：rgba(0, 120, 212, 0.1)
- 尺寸标注：跟随选区顶部，背景半透明黑，字号 12px

### 3.2 配色方案
```
主色调：#0078D4 (Windows 蓝)
背景遮罩：rgba(0, 0, 0, 0.4)
文字：系统默认字体（Segoe UI）
```

---

## 四、文件结构

```
PathSnip/
├── src/
│   └── PathSnip/
│       ├── PathSnip.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── CaptureOverlayWindow.xaml / CaptureOverlayWindow.xaml.cs
│       ├── Services/
│       │   ├── LogService.cs
│       │   ├── HotkeyService.cs
│       │   ├── ScreenCaptureService.cs
│       │   ├── ClipboardService.cs
│       │   ├── FileService.cs
│       │   └── ConfigService.cs
│       └── PathSnip.ico
├── publish/
│   └── (发布产物 ~830KB)
└── SPEC.md
```

---

## 五、数据存储

### 5.1 配置文件
路径：`%APPDATA%\PathSnip\config.json`

### 5.2 日志文件
路径：`%APPDATA%\PathSnip\logs\`

### 5.3 截图保存
路径：`Pictures\PathSnip\`

---

## 六、版本历史

| 版本 | 日期       | 说明           |
| ---- | ---------- | -------------- |
| 1.0.0 | 2026-02-22 | MVP：核心截图功能 |

---

**文档版本**：v1.0  
**最后更新**：2026-02-22
