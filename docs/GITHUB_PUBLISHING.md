# GitHub 发布步骤

本文件给项目维护者使用。普通用户只需要阅读根目录的 `README.md`。

## 素材权利状态

项目所有者已确认 `assets/mascot-fluent-v2.png` 和 `assets/app-fluent-v2.ico` 均为原创设计，并允许随本项目源码和官方 Release 公开分发。素材继续保留独立版权，不纳入代码的 MIT License；发布时必须保留 `ASSET-LICENSE.md`。

## 1. 检查本地仓库

```powershell
git status
.\test.ps1
.\pack-release.ps1 -Version 0.1.0-beta.1
```

只在全部通过后继续。打开 `release` 目录，应该只有当前准备上传的 ZIP、对应 `.sha256`，以及可能保留的旧本地包；上传时务必只选择带 `v0.1.0-beta.1` 的两个文件。

## 2. 建立 GitHub 仓库

建议仓库名称：`CodexHomeMover`

建议简介：

```text
Safely move the Windows .codex directory to another NTFS drive while preserving its original path.
```

建议主题：

```text
codex windows migration ntfs junction winforms backup disk-space
```

首次推送前检查 `git status`，确认没有 `dist/`、`release/`、测试产物、日志、PDB 或未使用的素材源文件。

## 3. 创建首个预发布

1. 在 GitHub 打开 **Releases → Draft a new release**。
2. 新建标签 `v0.1.0-beta.1`，目标分支选 `main`。
3. 标题填写 `Codex 搬家小鱼（Codex Home Mover）v0.1.0-beta.1`。
4. 勾选 **Set as a pre-release**。
5. 复制 `docs/RELEASE_NOTES_v0.1.0-beta.1.md` 的内容。
6. 只上传：
   - `CodexHomeMover-v0.1.0-beta.1-windows.zip`
   - `CodexHomeMover-v0.1.0-beta.1-windows.zip.sha256`
7. 发布后，用另一台或干净测试账户下载 ZIP，核对哈希并完成一次沙箱迁移。

## 4. 仓库设置

- 在 **Settings → Security & analysis** 开启 Private vulnerability reporting。
- 保持 GitHub Actions 的默认只读权限；CI 文件已显式声明 `contents: read`。
- 建议为 `main` 开启 pull request 和 CI 通过要求。
- 当前未签名版本不要标记为“稳定版”；收集 beta 反馈后再发布 v0.1.0。
