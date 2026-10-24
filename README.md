<div align="center">

<img src="assets/icon.png" width="120" alt="AppNetBlock" />

# AppNetBlock

**一键禁止文件夹内所有可执行文件联网的小工具**

Windows 10 / 11 · 单文件 · 免安装 · 零依赖 · 零常驻

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)]()
[![Dependency](https://img.shields.io/badge/dependency-.NET%20Framework%204.x%20%E7%B3%BB%E7%BB%9F%E8%87%AA%E5%B8%A6-blueviolet)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

简体中文 | [English](README_EN.md)

</div>

---

## 简介

把 `AppNetBlock.exe` 放进任意文件夹，双击运行，即可**禁止该文件夹（含全部子文件夹）内所有可执行文件的联网**：

- 对每个可执行文件建立**入站 + 出站**双向阻止规则
- 默认覆盖 **`.exe` / `.com` / `.scr` / `.pif`** 四种可直接作为进程运行的可执行格式，也可用 `--ext` 自定义扩展名
- 覆盖**域 / 专用 / 公用**全部网络配置文件（切换 Wi-Fi、有线、热点均有效）
- 规则写入 Windows 防火墙持久策略，**重启电脑不失效**
- **零后台进程**——规则由系统防火墙执行，工具运行完即退出
- 随时一键恢复，解除后不留任何痕迹

设计灵感来自 [simplewall](https://github.com/henrypp/simplewall) 与 [Portmaster](https://github.com/safing/portmaster) 的应用级断网能力。与它们不同的是，AppNetBlock 专注做好一件事——**文件夹级一键断网**，并把规则直接托管给系统防火墙，自身不常驻、不占资源。

典型用途：阻止游戏/软件的强制更新、许可验证、遥测上传，或让任何离线可用的程序彻底断网。

## 快速开始

1. 从 [Releases](../../releases) 下载 `AppNetBlock.exe`，放入需要断网的软件目录 / **开心版软件目录（专治不开心）**
2. 双击运行，确认 UAC 弹窗
3. 完成——该目录下所有可执行文件已断网

> 恢复联网：在原目录运行 `AppNetBlock.exe -u`

## 使用说明

| 命令 | 作用 |
| --- | --- |
| `AppNetBlock.exe` | 禁止所在文件夹（含全部子文件夹）内所有可执行文件联网 |
| `AppNetBlock.exe -p "D:\某目录"` | 禁止指定目录（含全部子文件夹）内所有可执行文件联网 |
| `AppNetBlock.exe --ext exe,com,scr` | 自定义要扫描的扩展名（默认 `exe com scr pif`） |
| `AppNetBlock.exe -u` | 恢复所在文件夹的联网 |
| `AppNetBlock.exe -u -p "D:\某目录"` | 恢复指定目录的联网 |
| `AppNetBlock.exe -u -a` | 恢复本工具管理过的所有目录的联网 |
| `AppNetBlock.exe -l` | 查看本工具管理的全部断网规则 |
| `AppNetBlock.exe --fw-on` | 开启 Windows 防火墙（仅防火墙被关闭时需要；默认不自动改动） |
| `AppNetBlock.exe -h` | 帮助 |

### 支持的可执行文件格式

- 默认扫描：`.exe`（程序）、`.com`（旧式可执行）、`.scr`（屏幕保护）、`.pif`（程序信息文件）——均为可直接作为**进程映像**运行的 PE 格式，防火墙规则可直接生效
- 自定义：通过 `--ext` 指定任意扩展名，如 `--ext exe,ocx`
- **脚本类文件（`.bat` / `.cmd` / `.ps1` / `.vbs` 等）默认不包含**：这类文件由 `cmd.exe` / `powershell.exe` 等解释器进程托管，防火墙规则匹配的是解释器映像路径而非脚本路径，强行加入不会产生断网效果

运行示例：

```
==============================================================
 AppNetBlock v1.1.0  -  文件夹一键断网 (基于 Windows 防火墙持久规则)
==============================================================
目标目录: D:\Games\SomeGame
动作    : 禁止该目录(含全部子目录)内所有可执行文件的联网(入站+出站)
扩展名  : .exe .com .scr .pif

处理结果:
  [已禁止联网] SomeGame.exe
  [已禁止联网] launcher.scr
  [已禁止联网] updater\updater.exe

完成: 共扫描 3 个可执行文件, 本次禁止 3 个。
```

## 工作原理

AppNetBlock 通过 Windows 防火墙 COM 接口（hnetcfg，底层为 WFP）为每个可执行文件创建持久 Block 规则：

- 每个可执行文件两条规则：**入站 Block + 出站 Block**
- `Profile = Any`：域 / 专用 / 公用网络全部生效
- 规则持久化于系统防火墙策略存储，由 Windows 防火墙服务执行——重启不失效，无需常驻进程
- Windows 防火墙中 Block 规则优先于 Allow 规则，可覆盖系统内置放行
- 规则按目录分组（`AppNetBlock::<目录>`），支持按目录整体解除，重复运行自动跳过已存在规则

可随时用系统自带命令查看本工具的规则：

```powershell
Get-NetFirewallRule -Group "AppNetBlock::*"
```

## 从源码构建

无需安装任何 SDK，使用 Windows 自带的 .NET Framework 4.x 编译器：

```powershell
./build.ps1
```

产物输出至 `dist/AppNetBlock.exe`。

## FAQ

**Q: 提示需要管理员权限？**
正常现象。修改防火墙策略需要管理员权限，程序清单已声明，双击会自动弹出 UAC。

**Q: 程序移动位置后规则还有效吗？**
防火墙按可执行文件的完整路径匹配。程序移动后需在新位置重新运行一次。

**Q: 已经在运行的程序会立即断网吗？**
新连接立即被阻断；已建立的活动连接会在其下次联网（重连、心跳）时被切断。重启该程序可立即完全断网。

**Q: 能拦截 .bat / .cmd 批处理脚本吗？**
不能。脚本本身不是进程，Windows 防火墙只能按"进程映像路径"匹配，脚本运行时匹配到的是 `cmd.exe` 等解释器。若需拦截，请找到脚本调用的真正联网程序（通常是同目录下的 .exe）。

**Q: 怎么彻底卸载？**
运行 `AppNetBlock.exe -u -a` 清除所有规则，然后删除 exe 即可。工具本身不留任何后台服务、驱动或配置。


## 已知限制

- 依赖系统防火墙服务（mpssvc / BFE）；若系统被深度精简导致服务缺失则无法工作（工具会给出提示）
- 防火墙处于关闭状态时工具不会静默开启（避免敏感的自动系统变更），会提示后中止，可用 `--fw-on` 显式开启
- 按可执行文件完整路径匹配；程序若借助其他系统进程联网（如通过 rundll32、浏览器转发流量），不在拦截范围内
- 脚本类文件（.bat/.cmd/.ps1/.vbs）由解释器进程托管，无法通过脚本路径拦截
- 仅按文件路径阻断 IP 层流量，不做域名过滤（那是 hosts/Pi-hole 类工具的领域）

## 许可证

[MIT](LICENSE)

## 致谢

- [henrypp/simplewall](https://github.com/henrypp/simplewall) — 应用级防火墙的轻量化思路
- [safing/portmaster](https://github.com/safing/portmaster) — 应用级联网监控的产品化形态
