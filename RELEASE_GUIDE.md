# PathSnip 发布工作流指南 (Release Protocol)

本文档规范 **PathSnip** 的版本发布流程。

## 核心原则
1. **验证优先**: 必须在本地构建通过后，才能进行发布操作。
2. **文档先行**: 代码变更后，必须先更新文档 (`CHANGELOG.md`)，再打 Tag。
3. **中文优先**: 所有对外发布的文档必须使用简体中文。
4. **原子化**: 发布相关的文档变更应单独提交，commit message 固定格式。

## 发布步骤

### 第一步：本地构建验证

```bash
# 清理并重新构建
dotnet clean src/PathSnip/PathSnip.csproj
dotnet build src/PathSnip/PathSnip.csproj -c Release
```

*如果构建失败，立即停止发布流程并修复代码。*

### 第二步：更新版本号

检查并更新：
- **src/PathSnip/PathSnip.csproj**: 更新 `<Version>`、`<AssemblyVersion>`、`<FileVersion>`

### 第三步：撰写更新日志

在 `CHANGELOG.md` 顶部添加新版本区块，**必须严格按照模板格式**：

```markdown
## [vX.Y.Z] - YYYY-MM-DD

> **[SYS_MSG]** 本次更新简要说明。
> **[METRICS]** Binary: 830KB (-5%) | Startup: -12ms

### 💻 终端与工作流
* **[关键词]** 描述（可选）

### ✨ 新特性
* **功能名称** - 详细描述

### 🐛 问题修复
* `[模块]` 问题描述 - 详细描述

### ⚙️ 优化与重构
* `[模块]` 优化说明 - 详细描述
```

**分类顺序（固定）**:
1. `### 💻 终端与工作流` - 终端、CLI、工作流相关
2. `### ✨ 新特性` - 新增功能
3. `### 🐛 问题修复` - Bug 修复
4. `### ⚙️ 优化与重构` - 性能优化、重构、清理

**子标签（可选）**:
- `[UI]` - UI 相关
- `[Core]` - 核心逻辑
- `[Perf]` - 性能相关
- `[Build]` - 构建相关

### 第四步：提交变更

```bash
git add src/PathSnip/PathSnip.csproj CHANGELOG.md
git commit -m "chore(release): prepare vX.Y.Z"
```

### 第五步：打标签与推送

```bash
git tag vX.Y.Z
git push origin main --tags
```

## CI/CD 机制说明
- **触发器**: 推送 `v*` 格式的 tag 会触发 GitHub Actions。
- **自动提取**: Release 内容自动从 `CHANGELOG.md` 提取最新版本日志。
- **发布产物**: 单文件 exe (由 Costura.Fody 合并)

## 紧急补救 (Hotfix)

如果在打 tag 后发现文档错误：
1. 不修改代码库历史
2. 使用 GitHub CLI 修正线上 Release Note：
   ```bash
   gh release edit vX.Y.Z --title "vX.Y.Z" --notes-file - <<'EOF'
   ## [vX.Y.Z] - YYYY-MM-DD
   
   > **[SYS_MSG]** ...
   
   ### ✨ 新特性
   ...
   EOF
   ```
3. 在 `main` 分支补交修正后的 `CHANGELOG.md`
