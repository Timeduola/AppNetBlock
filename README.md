<div align="center">

<img src="assets/icon.png" width="120" alt="AppNetBlock" />

# AppNetBlock

**一键禁止文件夹内所有 EXE 联网的小工具**

Windows 10 / 11 · 单文件 · 免安装 · 零依赖 · 零常驻

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)]()
[![Dependency](https://img.shields.io/badge/dependency-.NET%20Framework%204.x%20%E7%B3%BB%E7%BB%9F%E8%87%AA%E5%B8%A6-blueviolet)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

</div>

---

## 简介

把 `AppNetBlock.exe` 放进任意文件夹，双击运行，即可**禁止该文件夹（含全部子文件夹）内所有 EXE 的联网**：

- 对每个 EXE 建立**入站 + 出站**双向阻止规则
- 覆盖**域 / 专用 / 公用**全部网络配置文件（切换 Wi-Fi、有线、热点均有效）
- 规则写入 Windows 防火墙持久策略，**重启电脑不失效**
- **零后台进程**——规则由系统防火墙执行，工具运行完即退出
- 随时一键恢复，解除后不留任何痕迹

设计灵感来自 [simplewall](https://github.com/henrypp/simplewall) 与 [Portmaster](https://github.com/safing/portmaster) 的应用级断网能力。与它们不同的是，AppNetBlock 专注做好一件事——**文件夹级一键断网**，并把规则直接托管给系统防火墙，自身不常驻、不占资源。

典型用途：阻止游戏/软件的强制更新、许可验证、遥测上传，或让任何离线可用的程序彻底断网。

## 快速开始

1. 从 [Releases](../../releases) 下载 `AppNetBlock.exe`，放入需要断网的软件目录 / 开心版软件目录（专治不开心）
2. 双击运行，确认 UAC 弹窗
3. 完成——该目录下所有 EXE 已断网

> 恢复联网：在原目录运行 `AppNetBlock.exe -u`

## 使用说明

| 命令 | 作用 |
| --- | --- |
| `AppNetBlock.exe` | 禁止所在文件夹（含全部子文件夹）内所有 EXE 联网 |
| `AppNetBlock.exe -p "D:\某目录"` | 禁止指定目录（含全部子文件夹）内所有 EXE 联网 |
| `AppNetBlock.exe -u` | 恢复所在文件夹的联网 |
| `AppNetBlock.exe -u -p "D:\某目录"` | 恢复指定目录的联网 |
| `AppNetBlock.exe -u -a` | 恢复本工具管理过的所有目录的联网 |
| `AppNetBlock.exe -l` | 查看本工具管理的全部断网规则 |
| `AppNetBlock.exe --fw-on` | 开启 Windows 防火墙（仅防火墙被关闭时需要；默认不自动改动） |
| `AppNetBlock.exe -h` | 帮助 |

运行示例：

```
==============================================================
 AppNetBlock v1.0.1  -  文件夹一键断网 (基于 Windows 防火墙持久规则)
==============================================================
目标目录: D:\Games\SomeGame
动作    : 禁止该目录(含全部子目录)内所有 EXE 的联网(入站+出站)

处理结果:
  [已禁止联网] SomeGame.exe
  [已禁止联网] updater\updater.exe

完成: 共扫描 2 个 EXE, 本次禁止 2 个。
```

## 工作原理

AppNetBlock 通过 Windows 防火墙 COM 接口（hnetcfg，底层为 WFP）为每个 EXE 创建持久 Block 规则：

- 每个 EXE 两条规则：**入站 Block + 出站 Block**
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
防火墙按 EXE 的完整路径匹配。程序移动后需在新位置重新运行一次。

**Q: 已经在运行的程序会立即断网吗？**
新连接立即被阻断；已建立的活动连接会在其下次联网（重连、心跳）时被切断。重启该程序可立即完全断网。

**Q: 怎么彻底卸载？**
运行 `AppNetBlock.exe -u -a` 清除所有规则，然后删除 exe 即可。工具本身不留任何后台服务、驱动或配置。

**Q: 杀毒软件报毒？**
修改防火墙规则的行为可能触发部分杀软的启发式告警，属误报范畴。如有顾虑可自行审查源码（单文件 `src/Program.cs`）后本地编译。

**Q: 在线沙箱（哈勃/Any.Run 等）报了几条高危行为？**
逐条看来源：

- **“调整内存访问权限为只读可执行”（VirtualProtect）** —— .NET CLR JIT 编译器把 IL 编译成本地代码时的固有行为，**任何 .NET 程序都会报**，不是本工具代码所为；
- **“修改防火墙规则 / 注册表 FirewallPolicy”** —— 本工具的核心功能本体（写入断网规则），预期行为；
- 工具**不会**静默开启防火墙、不留后台进程、不写其他任何位置：自 v1.0.1 起，防火墙被关闭时仅提示并中止，需用户显式运行 `--fw-on` 才会开启。

主流杀软引擎对本样本的检出为 0/28。

## 已知限制

- 依赖系统防火墙服务（mpssvc / BFE）；若系统被深度精简导致服务缺失则无法工作（工具会给出提示）
- 防火墙处于关闭状态时工具不会静默开启（避免敏感的自动系统变更），会提示后中止，可用 `--fw-on` 显式开启
- 按 EXE 完整路径匹配；程序若借助其他系统进程联网（如通过 rundll32、浏览器转发流量），不在拦截范围内
- 仅按文件路径阻断 IP 层流量，不做域名过滤（那是 hosts/Pi-hole 类工具的领域）

## 许可证

[MIT](LICENSE)

## 致谢

- [henrypp/simplewall](https://github.com/henrypp/simplewall) — 应用级防火墙的轻量化思路
- [safing/portmaster](https://github.com/safing/portmaster) — 应用级联网监控的产品化形态
