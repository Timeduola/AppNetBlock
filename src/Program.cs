// ============================================================================
//  AppNetBlock v1.0.1 - 文件夹一键断网工具
//  灵感: henrypp/simplewall、safing/portmaster 的应用级断网能力
//  许可: MIT (详见仓库 LICENSE 文件)
//
//  原理: 将 Block 规则写入 Windows 防火墙(基于 WFP)的持久策略,
//        对目标文件夹(含全部子文件夹)内每个可执行文件建立 入站+出站 双向阻止规则,
//        并覆盖 域/专用/公用 全部网络配置文件。
//        规则由系统防火墙服务直接执行: 重启电脑不失效, 切换网络不失效,
//        无需任何后台常驻进程; Block 规则优先级高于系统内 Allow 规则。
//
//  默认覆盖的可执行格式: .exe / .com / .scr / .pif
//  (均为可直接作为进程映像运行的 PE 格式, 防火墙按进程映像路径匹配, 可直接生效)
//  可用 --ext 参数自定义扩展名; 注意 .bat/.cmd/.ps1 等脚本由解释器进程托管,
//  防火墙规则匹配的是解释器映像而非脚本路径, 故默认不包含(强行加入也不会生效)。
//
//  兼容: Windows 10 / Windows 11 (依赖系统自带的 .NET Framework 4.x)
//  权限: 需要管理员权限(程序清单已声明 requireAdministrator, 双击自动弹 UAC)
// ============================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Text;
using System.Threading;

// ---- 程序集信息 ----
// csc 会将这些特性自动生成为 Win32 版本信息资源,
// 供资源管理器"详细信息"页、任务管理器、UAC 弹窗显示(缺失则全部为空/0.0.0.0)。
// 注意与常量 Version 保持同步。
[assembly: AssemblyTitle("AppNetBlock 文件夹一键断网工具")]
[assembly: AssemblyDescription("一键禁止文件夹(含全部子文件夹)内所有可执行文件的联网, 基于 Windows 防火墙持久规则")]
[assembly: AssemblyCompany("AppNetBlock Contributors")]
[assembly: AssemblyProduct("AppNetBlock")]
[assembly: AssemblyCopyright("Copyright (c) 2026 AppNetBlock Contributors (MIT License)")]
[assembly: AssemblyFileVersion("1.1.0.0")]
[assembly: AssemblyInformationalVersion("1.1.0")]
[assembly: AssemblyVersion("1.1.0.0")]

namespace AppNetBlock
{
    static class Program
    {
        const string Version = "1.1.0";

        // 组名(Grouping)前缀: 用于识别本工具创建的规则, 并按目录分组管理
        const string GroupPrefix = "AppNetBlock::";
        // 规则名(Name)前缀
        const string NamePrefix = "AppNetBlock ";
        // INetFwRule 接口枚举值 (注意: NET_FW_ACTION 中 BLOCK=0, ALLOW=1)
        const int NET_FW_ACTION_BLOCK = 0;           // 阻止
        const int NET_FW_RULE_DIR_IN = 1;            // 入站
        const int NET_FW_RULE_DIR_OUT = 2;           // 出站
        const int NET_FW_PROFILE2_ALL = 0x7FFFFFFF;  // 全部配置文件(域/专用/公用)

        // 默认扫描的可执行文件扩展名。
        // 仅收录可直接作为进程映像运行的 PE 格式: 防火墙规则按"进程映像完整路径"匹配,
        // 这类文件(如 .scr 屏幕保护、.com、.pif)启动后即为自身进程, 规则可直接生效。
        // .bat/.cmd/.ps1/.vbs 等脚本由 cmd/PowerShell/wscript 等解释器进程托管,
        // 规则匹配到的将是解释器映像而非脚本路径, 故不纳入默认列表。
        static readonly string[] DefaultExtensions = { ".exe", ".com", ".scr", ".pif" };

        // STAThread: hnetcfg 防火墙 COM 组件为 Apartment 线程模型,
        // 主线程必须为 STA, 否则 Rules.Add(对象参数) 会因跨套间封送失败而报错。
        [STAThread]
        static int Main(string[] args)
        {
            bool unblock = false, list = false, all = false, help = false, fwon = false;
            string target = null;
            string extArg = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "-u" || Eq(a, "--unblock")) unblock = true;
                else if (Eq(a, "--fw-on")) fwon = true;
                else if (a == "-l" || Eq(a, "--list")) list = true;
                else if (a == "-a" || Eq(a, "--all")) all = true;
                else if (Eq(a, "--ext") && i + 1 < args.Length) extArg = args[++i];
                else if (a == "-h" || Eq(a, "--help") || a == "/?") help = true;
                else if ((a == "-p" || Eq(a, "--path")) && i + 1 < args.Length) target = args[++i];
                else { Console.WriteLine("未知参数: " + a); Console.WriteLine(); help = true; }
            }
            if (help) { PrintHelp(); return 0; }

            string[] extensions = ParseExtensions(extArg);

            if (!IsAdmin())
            {
                Console.WriteLine("[错误] 需要管理员权限才能修改防火墙策略。");
                Console.WriteLine("请右键此程序, 选择\"以管理员身份运行\"。");
                Pause();
                return 1;
            }

            try
            {
                if (fwon) return DoFirewallOn();
                if (list) return DoList();
                if (unblock) return DoUnblock(target, all);
                return DoBlock(target, extensions);
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("[错误] " + ex.Message);
                Pause();
                return 1;
            }
        }

        static bool Eq(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        // 解析 --ext 参数: "exe,com" / ".exe;.scr" 等写法均兼容; 无参数时返回默认列表
        static string[] ParseExtensions(string extArg)
        {
            if (string.IsNullOrWhiteSpace(extArg)) return DefaultExtensions;
            var set = new List<string>();
            foreach (string part in extArg.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string e = part.Trim().TrimStart('.').ToLowerInvariant();
                if (e.Length == 0) continue;
                if (!set.Contains("." + e)) set.Add("." + e);
            }
            if (set.Count == 0) return DefaultExtensions;
            return set.ToArray();
        }

        // ------------------------------------------------------------------ 禁止联网

        static int DoBlock(string target, string[] extensions)
        {
            string dir = ResolveTargetDir(target);
            string group = GroupPrefix + dir;

            Console.WriteLine("==============================================================");
            Console.WriteLine(" AppNetBlock v" + Version + "  -  文件夹一键断网 (基于 Windows 防火墙持久规则)");
            Console.WriteLine("==============================================================");
            Console.WriteLine("目标目录: " + dir);
            Console.WriteLine("动作    : 禁止该目录(含全部子目录)内所有可执行文件的联网(入站+出站)");
            Console.WriteLine("扩展名  : " + string.Join(" ", extensions));
            Console.WriteLine();

            if (!EnsureFirewallReady()) { Pause(); return 1; }

            Console.WriteLine("正在扫描可执行文件, 请稍候...");
            List<string> exes = FindAllExecutables(dir, extensions);
            if (exes.Count == 0)
            {
                Console.WriteLine("[提示] 该目录下没有找到匹配 " + string.Join(" ", extensions) + " 的可执行文件, 无需处理。");
                Pause();
                return 0;
            }

            dynamic fw = CreateFwPolicy();
            List<RuleInfo> existing = EnumerateRules(fw);
            HashSet<string> existingNames = new HashSet<string>(
                existing.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

            int blocked = 0, skipped = 0, failed = 0;
            Console.WriteLine();
            Console.WriteLine("处理结果:");
            foreach (string exe in exes)
            {
                string rel = GetRelativeSafe(dir, exe);
                string nameIn = MakeRuleName(exe, true);
                string nameOut = MakeRuleName(exe, false);

                if (existingNames.Contains(nameIn) && existingNames.Contains(nameOut))
                {
                    skipped++;
                    Console.WriteLine("  [已存在,跳过] " + rel);
                    continue;
                }

                bool ok = true;
                if (!existingNames.Contains(nameOut))
                {
                    if (AddBlockRule(fw, group, exe, false)) existingNames.Add(nameOut); else ok = false;
                }
                if (!existingNames.Contains(nameIn))
                {
                    if (AddBlockRule(fw, group, exe, true)) existingNames.Add(nameIn); else ok = false;
                }

                if (ok) { blocked++; Console.WriteLine("  [已禁止联网] " + rel); }
                else { failed++; Console.WriteLine("  [失败      ] " + rel); }
            }

            Console.WriteLine();
            Console.WriteLine("--------------------------------------------------------------");
            Console.WriteLine("完成: 共扫描 " + exes.Count + " 个可执行文件, 本次禁止 " + blocked +
                              " 个, 已存在跳过 " + skipped + " 个" +
                              (failed > 0 ? ", 失败 " + failed + " 个" : "") + "。");
            if (blocked > 0)
            {
                Console.WriteLine("已为每个可执行文件建立 入站+出站 双向阻止规则, 覆盖 域/专用/公用 全部网络配置。");
                Console.WriteLine("规则保存在 Windows 防火墙系统策略中: 重启电脑不失效, 切换网络不失效。");
                Console.WriteLine("恢复联网: 运行 AppNetBlock.exe -u  (指定目录加 -p \"目录\")");
            }

            PrintRunningProcesses(dir);
            Pause();
            return failed > 0 ? 1 : 0;
        }

        // ------------------------------------------------------------------ 解除联网

        static int DoUnblock(string target, bool all)
        {
            dynamic fw = CreateFwPolicy();
            List<RuleInfo> allRules = EnumerateRules(fw);
            List<RuleInfo> mine;

            if (all)
            {
                Console.WriteLine("动作: 恢复本工具管理过的【所有】目录的联网");
                mine = allRules.Where(IsManagedRule).ToList();
            }
            else
            {
                string dir = ResolveTargetDir(target);
                string group = GroupPrefix + dir;
                Console.WriteLine("目标目录: " + dir);
                Console.WriteLine("动作    : 恢复该目录下所有可执行文件的联网");
                mine = allRules.Where(r => r.Group != null && Eq(r.Group, group)).ToList();
            }

            if (mine.Count == 0)
            {
                Console.WriteLine("[提示] 没有找到由本工具创建的断网规则(可能尚未禁止或已解除)。");
                Pause();
                return 0;
            }

            int removed = 0;
            object rulesColl = fw.Rules;
            foreach (RuleInfo r in mine)
            {
                try { ComInvoke(rulesColl, "Remove", new object[] { r.Name }); removed++; }
                catch (Exception ex) { Console.WriteLine("  [删除失败] " + r.Name + " : " + ex.Message); }
            }

            Console.WriteLine();
            Console.WriteLine("完成: 已删除 " + removed + "/" + mine.Count + " 条防火墙规则, 相关程序恢复联网。");
            Pause();
            return 0;
        }

        // ------------------------------------------------------------------ 查看规则

        static int DoList()
        {
            dynamic fw = CreateFwPolicy();
            List<RuleInfo> allRules = EnumerateRules(fw);
            List<RuleInfo> mine = allRules.Where(IsManagedRule).ToList();

            if (mine.Count == 0)
            {
                Console.WriteLine("当前没有由 AppNetBlock 管理的断网规则。");
                Pause();
                return 0;
            }

            Console.WriteLine("AppNetBlock 当前管理的断网规则 (共 " + mine.Count + " 条):");
            foreach (var g in mine.GroupBy(r => r.Group, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key))
            {
                string dir = g.Key.Substring(GroupPrefix.Length);
                int appCount = g.Select(r => r.App).Distinct(StringComparer.OrdinalIgnoreCase).Count();
                Console.WriteLine();
                Console.WriteLine("目录: " + dir + "   [断网程序 " + appCount + " 个 / 规则 " + g.Count() + " 条]");
                foreach (RuleInfo r in g.OrderBy(x => x.App).ThenBy(x => x.Direction))
                {
                    Console.WriteLine("    [" + (r.Direction == NET_FW_RULE_DIR_IN ? "入站" : "出站") + "] " +
                                      r.App + (r.Enabled ? "" : "  (已停用)"));
                }
            }
            Pause();
            return 0;
        }

        static bool IsManagedRule(RuleInfo r)
        {
            return r.Group != null && r.Group.StartsWith(GroupPrefix, StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------ 防火墙环境准备

        // --fw-on: 显式开启防火墙(仅在防火墙被关闭的环境需要; 默认不自动改动系统设置)
        static int DoFirewallOn()
        {
            Console.WriteLine("动作: 开启 Windows 防火墙 全部配置文件(域/专用/公用)");
            Console.WriteLine();
            if (AllProfilesEnabled())
            {
                Console.WriteLine("防火墙已处于开启状态, 无需处理。");
                Pause();
                return 0;
            }
            EnableAllProfiles();
            if (AllProfilesEnabled())
            {
                Console.WriteLine("完成: 防火墙已开启。");
                Pause();
                return 0;
            }
            Console.WriteLine("[警告] 未能开启防火墙(可能被组策略或安全软件管控), 请手动处理。");
            Pause();
            return 1;
        }

        static bool EnsureFirewallReady()
        {
            Console.WriteLine("检查 Windows 防火墙组件...");
            EnsureServiceRunning("BFE");            // 基础筛选引擎(防火墙依赖项)
            if (!EnsureServiceRunning("mpssvc"))    // Windows 防火墙服务
            {
                Console.WriteLine("[错误] 无法启动 Windows 防火墙服务(mpssvc)。");
                Console.WriteLine("本工具依赖系统防火墙执行规则。若系统组件被精简导致服务缺失,");
                Console.WriteLine("则无法使用本方案(可改用 simplewall / Portmaster 等常驻型工具)。");
                return false;
            }

            if (AllProfilesEnabled())
            {
                Console.WriteLine("防火墙服务与配置正常。");
            }
            else
            {
                // 不自动开启: 静默修改系统防火墙全局状态属于敏感操作, 容易被安全软件/沙箱
                // 判定为高危行为(修改防火墙防护策略 + 调用 netsh)。改为提示 + 显式参数 --fw-on。
                // 正常环境(Windows 默认防火墙开启)下不会进入本分支, 断网效果不受影响。
                Console.WriteLine("[警告] 检测到部分防火墙配置文件(域/专用/公用)处于关闭状态。");
                Console.WriteLine("规则只有在防火墙开启时才会生效, 本工具默认不改动这一系统级设置。");
                Console.WriteLine();
                Console.WriteLine("如需本工具代为开启防火墙, 请运行: AppNetBlock.exe --fw-on");
                Console.WriteLine("或自行在 Windows 安全中心开启防火墙后, 再运行断网。");
                Console.WriteLine();
                return false;
            }
            Console.WriteLine();
            return true;
        }

        static bool EnsureServiceRunning(string name)
        {
            string q = RunCapture("sc.exe", "query " + name);
            if (q == null) return false;
            if (q.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            string qc = RunCapture("sc.exe", "qc " + name);
            if (qc != null && qc.IndexOf("DISABLED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Console.WriteLine("服务 " + name + " 处于禁用状态, 正在改为按需启动...");
                RunCapture("sc.exe", "config " + name + " start= demand");
            }
            Console.WriteLine("正在启动服务 " + name + " ...");
            RunCapture("sc.exe", "start " + name);
            for (int i = 0; i < 20; i++)
            {
                Thread.Sleep(300);
                string q2 = RunCapture("sc.exe", "query " + name);
                if (q2 != null && q2.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        static bool AllProfilesEnabled()
        {
            string baseKey = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\";
            string[] profiles = { "DomainProfile", "StandardProfile", "PublicProfile" };
            foreach (string p in profiles)
            {
                object v = Microsoft.Win32.Registry.GetValue(baseKey + p, "EnableFirewall", null);
                if (v is int && (int)v == 0) return false;
            }
            return true;
        }

        static void EnableAllProfiles()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh.exe", "advfirewall set allprofiles state on");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(15000);
                    if (p.ExitCode == 0) return;
                }
            }
            catch { }
            // 备用方案: COM 接口直接设置
            try
            {
                dynamic fw = CreateFwPolicy();
                fw.FirewallEnabled[1] = true;  // 域
                fw.FirewallEnabled[2] = true;  // 专用
                fw.FirewallEnabled[4] = true;  // 公用
            }
            catch { }
        }

        static string ReadExcepInfo(IntPtr pExcep)
        {
            try
            {
                if (pExcep == IntPtr.Zero) return "";
                bool x64 = IntPtr.Size == 8;
                int offDesc = x64 ? 16 : 8;
                int offSource = x64 ? 8 : 4;
                int offScode = x64 ? 56 : 28;
                int scode = Marshal.ReadInt32(pExcep, offScode);
                IntPtr bstrDesc = Marshal.ReadIntPtr(pExcep, offDesc);
                IntPtr bstrSrc = Marshal.ReadIntPtr(pExcep, offSource);
                string desc = bstrDesc != IntPtr.Zero ? Marshal.PtrToStringBSTR(bstrDesc) : "";
                string src = bstrSrc != IntPtr.Zero ? Marshal.PtrToStringBSTR(bstrSrc) : "";
                return string.Format("[scode=0x{0:X8} src={1} desc={2}]", scode, src, desc);
            }
            catch { return ""; }
        }

        // ------------------------------------------------------------------ 防火墙规则操作

        static bool AddBlockRule(dynamic fw, string group, string exePath, bool inbound)
        {
            try
            {
                dynamic rule = CreateRule();
                rule.Name = MakeRuleName(exePath, inbound);
                // 注意: 规则文本字段(Name/Description/Grouping)中禁止出现竖线"|",
                // 否则 hnetcfg 会拒绝写入(E_INVALIDARG)——防火墙内部用"|"作字段分隔符。
                rule.Description = "AppNetBlock " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + exePath;
                rule.ApplicationName = exePath;
                rule.Grouping = group;
                rule.Action = NET_FW_ACTION_BLOCK;
                rule.Direction = inbound ? NET_FW_RULE_DIR_IN : NET_FW_RULE_DIR_OUT;
                rule.Enabled = true;
                rule.Profiles = NET_FW_PROFILE2_ALL;

                // 通过手动 IDispatch::Invoke 调用 Rules.Add(对象参数),
                // 规避 DLR/InvokeMember 对 COM 对象参数的封送缺陷。
                object rulesColl = fw.Rules;
                ComInvoke(rulesColl, "Add", new object[] { (object)rule });
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("    [规则写入失败] " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ 手动 IDispatch 调用

        const short VT_BSTR = 8;
        const short VT_DISPATCH = 9;
        const ushort DISPATCH_METHOD = 1;

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        struct VariantInterop
        {
            [FieldOffset(0)] public short vt;
            [FieldOffset(8)] public IntPtr pointerValue;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct DispParamsInterop
        {
            public IntPtr rgvarg;
            public IntPtr rgdispidNamedArgs;
            public int cArgs;
            public int cNamedArgs;
        }

        [ComImport, Guid("00020400-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IDispatchInterop
        {
            [PreserveSig] int GetTypeInfoCount(out int pctinfo);
            [PreserveSig] int GetTypeInfo(int iTI, int lcid, out IntPtr ppTInfo);
            [PreserveSig] int GetIDsOfNames(ref Guid riid,
                [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] rgszNames,
                int cNames, int lcid,
                [Out, MarshalAs(UnmanagedType.LPArray)] int[] rgDispId);
            [PreserveSig] int Invoke(int dispIdMember, ref Guid riid, int lcid, ushort wFlags,
                ref DispParamsInterop pDispParams, IntPtr pVarResult, IntPtr pExcepInfo, out int puArgErr);
        }

        // 通过 IDispatch::Invoke 调用 COM 方法; 参数仅支持 string 与 COM 对象(按 VT_DISPATCH 封送)
        static void ComInvoke(object target, string methodName, object[] args)
        {
            IDispatchInterop disp = (IDispatchInterop)target;
            Guid nullGuid = Guid.Empty;
            string[] names = { methodName };
            int[] dispIds = new int[1];
            int hr = disp.GetIDsOfNames(ref nullGuid, names, 1, 0, dispIds);
            if (hr < 0) Marshal.ThrowExceptionForHR(hr);

            int n = args == null ? 0 : args.Length;
            int variantSize = 24;
            IntPtr rgvarg = IntPtr.Zero;
            IntPtr pResult = IntPtr.Zero;
            IntPtr pExcep = IntPtr.Zero;
            var bstrs = new List<IntPtr>();
            var idispatches = new List<IntPtr>();
            try
            {
                rgvarg = Marshal.AllocHGlobal(n * variantSize);
                for (int i = 0; i < n * variantSize; i++) Marshal.WriteByte(rgvarg, i, 0);

                // 参数逆序写入 rgvarg (IDispatch 约定: args[i] -> rgvarg[n-1-i])
                for (int i = 0; i < n; i++)
                {
                    object a = args[i];
                    var v = new VariantInterop();
                    v.vt = 0;
                    v.pointerValue = IntPtr.Zero;
                    if (a is string)
                    {
                        IntPtr b = Marshal.StringToBSTR((string)a);
                        bstrs.Add(b);
                        v.vt = VT_BSTR;
                        v.pointerValue = b;
                    }
                    else if (a != null && Marshal.IsComObject(a))
                    {
                        IntPtr d = Marshal.GetIDispatchForObject(a);
                        idispatches.Add(d);
                        v.vt = VT_DISPATCH;
                        v.pointerValue = d;
                    }
                    else
                    {
                        throw new NotSupportedException("ComInvoke 仅支持 string / COM 对象参数: " + methodName);
                    }
                    Marshal.StructureToPtr(v, (IntPtr)((long)rgvarg + (long)(n - 1 - i) * variantSize), false);
                }

                var dp = new DispParamsInterop();
                dp.rgvarg = rgvarg;
                dp.rgdispidNamedArgs = IntPtr.Zero;
                dp.cArgs = n;
                dp.cNamedArgs = 0;

                pResult = Marshal.AllocHGlobal(32);
                for (int i = 0; i < 32; i++) Marshal.WriteByte(pResult, i, 0);
                pExcep = Marshal.AllocHGlobal(64);
                for (int i = 0; i < 64; i++) Marshal.WriteByte(pExcep, i, 0);

                int argErr;
                hr = disp.Invoke(dispIds[0], ref nullGuid, 0, DISPATCH_METHOD, ref dp, pResult, pExcep, out argErr);
                if (hr < 0)
                {
                    string detail = ReadExcepInfo(pExcep);
                    throw new Exception(string.Format("COM 方法 {0} 失败: 0x{1:X8} {2}", methodName, hr, detail));
                }
            }
            finally
            {
                if (rgvarg != IntPtr.Zero) Marshal.FreeHGlobal(rgvarg);
                if (pResult != IntPtr.Zero) Marshal.FreeHGlobal(pResult);
                if (pExcep != IntPtr.Zero) Marshal.FreeHGlobal(pExcep);
                foreach (IntPtr b in bstrs) Marshal.FreeBSTR(b);
                foreach (IntPtr d in idispatches) Marshal.Release(d);
            }
        }

        static List<RuleInfo> EnumerateRules(dynamic fw)
        {
            var list = new List<RuleInfo>();
            IEnumerable coll = (IEnumerable)fw.Rules;
            foreach (object o in coll)
            {
                try
                {
                    dynamic r = o;
                    var info = new RuleInfo();
                    info.Name = (string)r.Name;
                    info.Group = r.Grouping == null ? null : (string)r.Grouping;
                    info.App = r.ApplicationName == null ? null : (string)r.ApplicationName;
                    try { info.Direction = (int)r.Direction; } catch { }
                    try { info.Enabled = (bool)r.Enabled; } catch { }
                    if (info.Name != null) list.Add(info);
                }
                catch { }
            }
            return list;
        }

        class RuleInfo
        {
            public string Name;
            public string Group;
            public string App;
            public int Direction;
            public bool Enabled;
        }

        static dynamic CreateFwPolicy()
        {
            Type t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2", true);
            return Activator.CreateInstance(t);
        }

        static dynamic CreateRule()
        {
            Type t = Type.GetTypeFromProgID("HNetCfg.FwRule", true);
            return Activator.CreateInstance(t);
        }

        // 规则名 = 前缀[方向] 文件名 [路径哈希], 名称内含哈希保证全局唯一且可重复运行幂等
        static string MakeRuleName(string exePath, bool inbound)
        {
            string hash = Md5Hex(exePath.ToLowerInvariant()).Substring(0, 8);
            return string.Format("{0}[{1}] {2} [{3}]",
                NamePrefix,
                inbound ? "入站" : "出站",
                Path.GetFileName(exePath),
                hash);
        }

        static string Md5Hex(string s)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(s));
                var sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------------ 文件扫描

        // 递归枚举 root 及其全部子目录中匹配 extensions 的文件
        // (跳过无权限目录; visited 防止符号链接循环; 哈希集合去重, 兼容 8.3 短文件名干扰)
        static List<string> FindAllExecutables(string root, string[] extensions)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
            var patterns = extensions.Select(e => "*" + e).ToArray();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string d = stack.Pop();
                string key = d.TrimEnd('\\').ToLowerInvariant();
                if (!visited.Add(key)) continue;

                foreach (string pattern in patterns)
                {
                    string[] files;
                    try { files = Directory.GetFiles(d, pattern); }
                    catch { files = new string[0]; }
                    foreach (string f in files)
                    {
                        // 严格按扩展名过滤(避免 GetFiles 通配符因短文件名产生误匹配)
                        string ext = Path.GetExtension(f);
                        if (extSet.Contains(ext) && seen.Add(f)) result.Add(f);
                    }
                }

                string[] subdirs;
                try { subdirs = Directory.GetDirectories(d); }
                catch { subdirs = new string[0]; }
                foreach (string s in subdirs) stack.Push(s);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        // ------------------------------------------------------------------ 辅助函数

        static string ResolveTargetDir(string target)
        {
            string dir = string.IsNullOrEmpty(target) ? AppDomain.CurrentDomain.BaseDirectory : target;
            dir = Path.GetFullPath(dir);
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("目录不存在: " + dir);
            dir = dir.TrimEnd('\\');
            if (dir.Length == 2 && dir[1] == ':') dir = dir + "\\";  // 盘符根目录保留反斜杠
            return dir;
        }

        static string GetRelativeSafe(string root, string path)
        {
            try
            {
                string rel = path.Substring(root.Length).TrimStart('\\');
                return rel.Length == 0 ? Path.GetFileName(path) : rel;
            }
            catch { return path; }
        }

        static void PrintRunningProcesses(string dir)
        {
            try
            {
                string prefix = dir.EndsWith("\\") ? dir : dir + "\\";
                var hits = new List<string>();
                foreach (Process p in Process.GetProcesses())
                {
                    try
                    {
                        string path = p.MainModule.FileName;
                        if (!string.IsNullOrEmpty(path) && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                            hits.Add(Path.GetFileName(path) + "  (PID " + p.Id + ")");
                    }
                    catch { }
                }
                if (hits.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("提示: 以下 " + hits.Count + " 个该目录的进程正在运行:");
                    foreach (string h in hits) Console.WriteLine("  " + h);
                    Console.WriteLine("规则对新连接立即生效; 运行中进程的活动连接会在其下次联网时被阻断,");
                    Console.WriteLine("重启该程序可立即完全断网。");
                }
            }
            catch { }
        }

        static string RunCapture(string exe, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo();
                psi.FileName = exe;
                psi.Arguments = arguments;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    try { p.StandardError.ReadToEnd(); } catch { }
                    p.WaitForExit(15000);
                    return output;
                }
            }
            catch { return null; }
        }

        static bool IsAdmin()
        {
            WindowsIdentity id = WindowsIdentity.GetCurrent();
            WindowsPrincipal principal = new WindowsPrincipal(id);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        static void Pause()
        {
            if (Console.IsInputRedirected) return;
            try
            {
                Console.WriteLine();
                Console.Write("按任意键退出...");
                Console.ReadKey(true);
            }
            catch { }
        }

        static void PrintHelp()
        {
            Console.WriteLine("AppNetBlock v" + Version + " - 文件夹一键断网工具 (基于 Windows 防火墙持久规则)");
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("  AppNetBlock.exe                    禁止本程序所在文件夹(含子文件夹)内所有可执行文件联网");
            Console.WriteLine("  AppNetBlock.exe -p \"D:\\目录\"       禁止指定目录(含子文件夹)内所有可执行文件联网");
            Console.WriteLine("  AppNetBlock.exe --ext exe,com      自定义要扫描的扩展名(默认 exe com scr pif)");
            Console.WriteLine("  AppNetBlock.exe -u                 恢复本程序所在文件夹的联网");
            Console.WriteLine("  AppNetBlock.exe -u -p \"D:\\目录\"    恢复指定目录的联网");
            Console.WriteLine("  AppNetBlock.exe -u -a              恢复本工具管理过的所有目录的联网");
            Console.WriteLine("  AppNetBlock.exe -l                 查看本工具管理的全部断网规则");
            Console.WriteLine("  AppNetBlock.exe --fw-on            开启 Windows 防火墙(仅防火墙被关闭时需要)");
            Console.WriteLine("  AppNetBlock.exe -h                 显示本帮助");
            Console.WriteLine();
            Console.WriteLine("特性:");
            Console.WriteLine("  - 一键对文件夹(含全部子文件夹)内所有可执行文件建立 入站+出站 双向阻止规则");
            Console.WriteLine("  - 默认覆盖 .exe / .com / .scr / .pif 四种可直接作为进程运行的可执行格式");
            Console.WriteLine("  - 可用 --ext 自定义扩展名, 如: AppNetBlock.exe --ext exe,com,scr");
            Console.WriteLine("  - 规则覆盖 域/专用/公用 全部网络配置文件, 切换 Wi-Fi/有线/热点均有效");
            Console.WriteLine("  - 规则保存在 Windows 防火墙系统策略中, 重启电脑不失效, 无需后台常驻进程");
            Console.WriteLine("  - Block 规则优先级高于系统内其他 Allow 规则");
            Console.WriteLine("  - 典型用途: 把本工具放进游戏/软件目录双击运行, 即可阻止其联网");
            Console.WriteLine("    (阻止强制更新、验证、遥测上传等)");
            Console.WriteLine();
            Console.WriteLine("注意:");
            Console.WriteLine("  - 需要管理员权限(程序清单已声明, 双击自动弹出 UAC)");
            Console.WriteLine("  - .bat/.cmd/.ps1/.vbs 等脚本由解释器进程托管, 防火墙按进程映像路径匹配,");
            Console.WriteLine("    阻断脚本路径本身不生效; 将其加入 --ext 不会带来断网效果");
            Console.WriteLine("  - 若程序文件移动了位置, 规则不再匹配, 需在新位置重新运行一次");
            Console.WriteLine("  - 兼容 Windows 10 / Windows 11 (依赖系统自带 .NET Framework 4.x)");
            Console.WriteLine("  - 也可用 PowerShell 管理: Get-NetFirewallRule -Group \"AppNetBlock::*\"");
            Pause();
        }
    }
}
