# Codex Home Mover v0.1.0-beta.1

首个公开测试版。建议先在重要数据已有独立备份的 Windows 10/11 电脑上使用。

## 主要功能

- 可视化统计 `.codex` 占用并推荐本地 NTFS 目标盘。
- 先复制、再校验、最后建立 Junction，保持原路径不变。
- 支持 SHA-256、SQLite 完整性检查、长路径、取消和失败后续传。
- 保留 C 盘安全备份，支持迁回和二次确认后释放空间。
- 80%–200% 界面缩放、实时进度、小鱼动画与成功弹窗。

## 下载

下载并完整解压：

```text
CodexHomeMover-v0.1.0-beta.1-windows.zip
```

请同时下载 `.zip.sha256`，核对 ZIP 的 SHA-256。不要只取出或转发单独的 EXE；`CodexHomeMover.exe.config` 必须和程序放在同一目录。

## 已知限制

- 仅支持 Windows 10/11 与本地固定 NTFS 磁盘。
- 需要管理员权限。
- 当前 EXE 未做 Authenticode 代码签名，SmartScreen 可能显示“未知发布者”。请核对 SHA-256，不要关闭 Defender。
- 迁移开始后必须保持 Codex/ChatGPT 关闭，直到成功弹窗出现。

## 升级说明

如果你用过开发阶段的旧 EXE，请删除旧副本并只使用本 Release ZIP 中的新版本。不要上传或继续分发旧的 `latest`、`sandbox-tested`、`cute-success` 等过程版程序。
