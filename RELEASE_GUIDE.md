# PathSnip 发布工作流指南 (Release Protocol)

本文档规范 **PathSnip** 的版本发布流程。

## 核心原则
1. **验证优先**: 必须在本地构建通过后，才能进行发布操作。
2. **文档先行**: 代码变更后，必须先更新文档 (`CHANGELOG.md` 和 `RELEASE_TEMPLATE.md`)，再打 Tag。
3. **中文优先**: 所有对外发布的文档必须使用简体中文。
4. **原子化**: 发布相关的文档变更应单独提交，commit message 固定格式。

## 详细发布步骤

### 第一步：本地构建验证

```bash
# 清理并重新构建
dotnet clean src/PathSnip/PathSnip.csproj
dotnet build src/PathSnip/PathSnip.csproj -c Release

# 发布
dotnet publish src/PathSnip/PathSnip.csproj -c Release -r win-x64 --self-contained false -o publish
```

*如果构建失败，立即停止发布流程并修复代码。*

### 第二步：更新版本号

检查并更新以下文件：
1. **src/PathSnip/PathSnip.csproj**: 更新 `<Version>`、`<AssemblyVersion>`、`<FileVersion>`

### 第三步：更新变更日志 (CHANGELOG.md)

在 `CHANGELOG.md` 顶部添加新版本区块。

**风格指南**:
1. **标题格式**: `## [vX.Y.Z] - YYYY-MM-DD`
2. **分类标题**:
   - `### ⚠️ 重大变更 (Breaking Changes)`
   - `### ✨ 新特性 (Features)`
   - `### 🐛 问题修复 (Bug Fixes)`
   - `### 🚀 优化 (Optimizations)`
   - `### 🧹 清理 (Cleanup)`
3. **列表项格式**:
   - 统一使用 `*` 作为列表符
   - 关键词导向 + 详细描述

### 第四步：准备发布模板

将 `CHANGELOG.md` 中对应的新版本部分复制到 `RELEASE_TEMPLATE.md`。

### 第五步：提交变更

```bash
git add src/PathSnip/PathSnip.csproj CHANGELOG.md RELEASE_TEMPLATE.md
git commit -m "chore(release): prepare vX.Y.Z"
```

### 第六步：打标签与推送

```bash
git tag vX.Y.Z
git push origin main --tags
```

## CI/CD 机制说明
- **触发器**: 推送 `v*` 格式的 tag 会触发 GitHub Actions。
- **发布内容**:
  - Release Title: `vX.Y.Z`
  - Release Body: 读取自 `RELEASE_TEMPLATE.md`

## 紧急补救 (Hotfix)

如果在打 tag 后发现文档错误：
1. 不修改代码库历史
2. 使用 GitHub CLI 修正线上 Release Note：
   ```bash
   gh release edit vX.Y.Z --notes-file RELEASE_TEMPLATE.md
   ```
3. 在 `main` 分支补交修正后的 `CHANGELOG.md`
