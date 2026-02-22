# PathSnip

轻量级截图工具，专为开发者设计。

## 功能

- 快捷键截图（默认 `Ctrl+Shift+A`）
- 框选截图，蓝色边框
- 自动保存到指定目录
- **截图后路径自动复制到剪贴板**
- 托盘运行，后台常驻
- 支持自定义快捷键、保存目录
- 开机自启选项

## 使用方法

1. 运行 `PathSnip.exe`
2. 程序最小化到托盘
3. 按 `Ctrl+Shift+A` 截图
4. 框选区域 → 自动保存 → 路径已复制
5. 终端中 `Ctrl+V` 粘贴路径

## 快捷操作

| 操作 | 说明 |
| ---- | ---- |
| 左键拖拽 | 框选区域 |
| 右键/ESC | 取消截图 |
| 托盘左键 | 截图 |
| 托盘右键 | 菜单（设置/打开目录/退出） |

## 系统要求

- Windows 10 (1809+) / Windows 11
- .NET Framework 4.8（系统预装）

## 目录结构

```
PathSnip/
├── publish/          # 发布产物
│   └── PathSnip.exe
├── src/PathSnip/    # 源代码
└── SPEC.md          # 产品规格
```

## 技术栈

- .NET Framework 4.8
- WPF
- Hardcodet.NotifyIcon.Wpf

## 体积

< 1MB（不含运行时）
