<div align="center">

<img src="assets/icon.png" width="120" alt="AppNetBlock" />

# AppNetBlock

**One-click network blocker for every executable in a folder**

Windows 10 / 11 · Single file · Portable · Zero dependency · Zero background process

[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-blue)]()
[![Dependency](https://img.shields.io/badge/dependency-.NET%20Framework%204.x%20built--in-blueviolet)]()
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)

[简体中文](README.md) | English

</div>

---

## Introduction

Drop `AppNetBlock.exe` into any folder, double-click it, and it will **block network access for every executable inside that folder (including all sub-folders)**:

- Creates an **inbound + outbound** Block rule for each executable
- Covers **`.exe` / `.com` / `.scr` / `.pif`** — four PE formats that can run directly as a process — by default, with custom extensions available via `--ext`
- Applies to **all network profiles**: Domain / Private / Public (effective across Wi-Fi, Ethernet, and hotspots)
- Rules are written into the persistent Windows Firewall policy — **they survive reboots**
- **Zero background processes** — the rules are enforced by the system firewall; the tool exits as soon as it's done
- One-click restore at any time, leaving no trace once removed

Inspired by the per-application blocking capabilities of [simplewall](https://github.com/henrypp/simplewall) and [Portmaster](https://github.com/safing/portmaster). Unlike them, AppNetBlock does one thing well — **folder-level, one-click network blocking** — and delegates rule enforcement entirely to the system firewall. It stays resident in neither memory nor your startup.

Typical use cases: stopping forced updates, license checks, or telemetry uploads from games and software, or cutting a program off the network entirely while it remains usable offline.

## Quick Start

1. Download `AppNetBlock.exe` from [Releases](../../releases) and place it in the folder of the software you want to isolate from the network
2. Double-click it and confirm the UAC prompt
3. Done — every executable in that folder is now blocked from the network

> To restore: run `AppNetBlock.exe -u` in the same folder

## Usage

| Command | Description |
| --- | --- |
| `AppNetBlock.exe` | Block all executables in the current folder (including sub-folders) |
| `AppNetBlock.exe -p "D:\SomeFolder"` | Block all executables in the given folder (including sub-folders) |
| `AppNetBlock.exe --ext exe,com,scr` | Custom extensions to scan (default: `exe com scr pif`) |
| `AppNetBlock.exe -u` | Restore network access for the current folder |
| `AppNetBlock.exe -u -p "D:\SomeFolder"` | Restore network access for the given folder |
| `AppNetBlock.exe -u -a` | Restore network access for all folders managed by this tool |
| `AppNetBlock.exe -l` | List all blocking rules created by this tool |
| `AppNetBlock.exe --fw-on` | Turn on the Windows Firewall (only needed when it's off; the tool never changes this silently) |
| `AppNetBlock.exe -h` | Help |

### Supported Executable Formats

- Scanned by default: `.exe` (programs), `.com` (legacy executables), `.scr` (screensavers), `.pif` (program information files) — all are PE formats that run directly as a **process image**, so firewall rules apply to them directly
- Custom: specify any extensions with `--ext`, e.g. `--ext exe,ocx`
- **Script files (`.bat` / `.cmd` / `.ps1` / `.vbs`, etc.) are excluded by default**: they are hosted by interpreter processes such as `cmd.exe` / `powershell.exe`, and firewall rules match the interpreter's image path rather than the script path — adding them would have no effect

Example output:

```
==============================================================
 AppNetBlock v1.1.0  -  Folder One-Click Net Block (Windows Firewall persistent rules)
==============================================================
Target   : D:\Games\SomeGame
Action   : Block all executables (inbound + outbound) in this folder tree
Ext      : .exe .com .scr .pif

Results:
  [Blocked    ] SomeGame.exe
  [Blocked    ] launcher.scr
  [Blocked    ] updater\updater.exe

Done: 3 executables scanned, 3 newly blocked.
```

## How It Works

AppNetBlock creates persistent Block rules for each executable through the Windows Firewall COM interface (hnetcfg, backed by WFP):

- Two rules per executable: **inbound Block + outbound Block**
- `Profile = Any`: effective on Domain / Private / Public networks
- Rules are persisted in the system firewall policy store and enforced by the Windows Firewall service — they survive reboots and need no resident process
- In Windows Firewall, Block rules take precedence over Allow rules, overriding built-in allowances
- Rules are grouped per directory (`AppNetBlock::<folder>`), can be removed as a whole per folder, and re-running automatically skips existing rules

You can inspect the rules at any time with the built-in PowerShell command:

```powershell
Get-NetFirewallRule -Group "AppNetBlock::*"
```

## Build from Source

No SDK required — it uses the .NET Framework 4.x compiler that ships with Windows:

```powershell
./build.ps1
```

The output is written to `dist/AppNetBlock.exe`.

## FAQ

**Q: It says administrator privileges are required?**
Expected. Modifying firewall policy requires admin rights; the manifest declares it, so double-clicking triggers the UAC prompt automatically.

**Q: Do rules still work after the program is moved?**
The firewall matches the full path of the executable. Run the tool once again from the new location.

**Q: Will already-running programs be cut off immediately?**
New connections are blocked immediately; established connections get cut at their next network activity (reconnect, heartbeat). Restarting the program blocks it fully right away.

**Q: Can it block .bat / .cmd batch scripts?**
No. Scripts are not processes — Windows Firewall can only match "process image paths", and when a script runs, the matched process is `cmd.exe` or another interpreter. To block a script, find the real networking program it invokes (usually an .exe in the same folder).

**Q: How do I uninstall completely?**
Run `AppNetBlock.exe -u -a` to remove all rules, then delete the exe. The tool leaves no background services, drivers, or configuration behind.

## Known Limitations

- Depends on the Windows Firewall services (mpssvc / BFE); on deeply stripped-down systems where those services are missing, it won't work (the tool will tell you)
- When the firewall is off, the tool does not silently turn it on (avoiding sensitive automatic system changes) — it warns and aborts; use `--fw-on` to enable explicitly
- Matching is by full executable path; programs that route traffic through other system processes (rundll32, browser proxying, etc.) are out of scope
- Script files (.bat/.cmd/.ps1/.vbs) are hosted by interpreter processes and cannot be blocked by script path
- Blocks IP-layer traffic by file path only; DNS/domain filtering is out of scope (that's the domain of hosts/Pi-hole-style tools)

## License

[MIT](LICENSE)

## Acknowledgements

- [henrypp/simplewall](https://github.com/henrypp/simplewall) — the lightweight take on per-application firewalls
- [safing/portmaster](https://github.com/safing/portmaster) — the polished product form of per-application network control
