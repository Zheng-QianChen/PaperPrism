using System.Globalization;

namespace EInkSmartInvert.UI;

internal static class UiStrings
{
    public static bool IsZh =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", StringComparison.OrdinalIgnoreCase);

    public static string AppTitle => IsZh ? "纸棱镜" : "PaperPrism";
    public static string MastheadTitle => IsZh ? "纸 棱 镜" : "PAPER PRISM";
    public static string MastheadSubtitle => IsZh ? "暗夜入墨，折射成纸" : "Dark screen into calm E-Ink paper";

    public static string BtnRefresh => IsZh ? "↻ 刷新" : "↻ Refresh";
    public static string BtnActiveOn => IsZh ? "折射开启中" : "Prism Active";
    public static string BtnPausedOff => IsZh ? "已暂停直通" : "Passthrough";

    public static string TabPrism => IsZh ? "一 · 棱镜总控" : "I · Prism";
    public static string TabApps(int count, bool whitelistOn) =>
        IsZh
            ? (whitelistOn ? $"二 · 登场应用 · {count}" : "二 · 登场应用")
            : (whitelistOn ? $"II · Apps · {count}" : "II · Apps");
    public static string TabTuning => IsZh ? "三 · 显影调校" : "III · Optics";

    public static string SecPrism => IsZh ? "投射画布与折射模式" : "TARGET DISPLAY & MODE";
    public static string SecApps => IsZh ? "登场应用名录" : "CURATED APPLICATIONS";
    public static string SecTuning => IsZh ? "显影参数与光学开关" : "PAPER TUNING & OPTICS";

    public static string AutoSecondaryMonitor => IsZh ? "自动选择副屏画布" : "Auto-Select Secondary Display";
    public static string TargetStandby(string dev) => IsZh ? $"指定副屏：{dev} · 待唤醒" : $"Target: {dev} · Standby";

    public static string Mode1Title => IsZh ? "智能反色" : "Smart Invert";
    public static string Mode1Desc  => IsZh ? "暗底转纸白 · 亮底不动 · 护影音" : "Dark to paper · Keep light · Protect media";
    public static string Mode2Title => IsZh ? "强制反色" : "Force Invert";
    public static string Mode2Desc  => IsZh ? "整窗反转明度 · 保持原本色相" : "Invert full window · Preserve OKLab hue";
    public static string Mode3Title => IsZh ? "纸感提纯" : "Paper Crisp";
    public static string Mode3Desc  => IsZh ? "不反转明度 · 仅去底灰与增艳" : "No inversion · Whiten paper & boost chroma";
    public static string Mode4Title => IsZh ? "原画休眠" : "Passthrough";
    public static string Mode4Desc  => IsZh ? "暂停滤镜 · 零显卡开销直通" : "Disable filter · Zero GPU overhead";

    public static string WhitelistOverviewCheck => IsZh ? "仅对名录内的应用开启折射，其余窗口零开销直通" : "Only refract enabled apps in roster; passthrough others";
    public static string JumpAppsBtn(int count) => IsZh ? $"翻阅名录 · {count} →" : $"Open Roster · {count} →";

    public static string PresetKaleido => IsZh ? "预设 · 彩墨屏" : "Preset · Color E-Ink";
    public static string PresetCarta   => IsZh ? "预设 · 黑白屏" : "Preset · Monochrome";
    public static string BtnSnapshot   => IsZh ? "抓取现场快照" : "Capture Snapshot";

    public static string WhitelistAppsCheck => IsZh ? "仅对已勾选的应用开启折射" : "Only refract checked apps";
    public static string BtnPickWindow => IsZh ? "+ 收录运行窗口" : "+ Running App";
    public static string BtnAddManual  => IsZh ? "+ 手动添入" : "+ Add";
    public static string BtnResetList  => IsZh ? "恢复默认" : "Defaults";
    public static string AlreadyInList => IsZh ? "已收录 · " : "Added · ";
    public static string NoOtherApps   => IsZh ? "当前无其他可见桌面应用" : "No other visible desktop windows";

    public static string[] ShortRuleModes => IsZh
        ? new[] { "跟随全局", "智能反色", "强制反色", "纸感提纯", "原画直通" }
        : new[] { "Global", "Smart", "Force", "Crisp", "Bypass" };

    public static string[] FullRuleModes => IsZh
        ? new[]
        {
            "跟随全局模式",
            "智能反色 · 暗转白且护图文",
            "强制反色 · 整窗明度反转",
            "纸感提纯 · 仅去底灰增艳",
            "原画直通 · 不反色零开销"
        }
        : new[]
        {
            "Follow Global Mode",
            "Smart Invert · Dark to white, keep media",
            "Force Invert · Invert entire window",
            "Paper Crisp · Enhance light paper only",
            "Passthrough · Native zero-overhead"
        };

    public static string EditDlgTitle  => IsZh ? "编辑应用词条" : "Edit Application Rule";
    public static string EditDlgName   => IsZh ? "显示名称：" : "Display Name:";
    public static string EditDlgProcs  => IsZh ? "匹配进程名，多项用分号 ; 分隔：" : "Process names separated by ';' (no .exe):";
    public static string BtnOk         => IsZh ? "确定" : "Save";
    public static string BtnCancel     => IsZh ? "取消" : "Cancel";
    public static string NewAppDefault => IsZh ? "自定义应用" : "Custom App";

    public static string SldWhite => IsZh ? "纸白漂白阈值" : "Paper White";
    public static string SldBlack => IsZh ? "墨黑加深阈值" : "Ink Black";
    public static string SldSat   => IsZh ? "色彩饱和增艳" : "Chroma Boost";
    public static string SldPhoto => IsZh ? "护图识别灵敏" : "Media Guard";
    public static string SldFps   => IsZh ? "最高刷新帧率" : "Max Frame Rate";
    public static string ValMono  => IsZh ? "黑白" : "Mono";
    public static string ValFps(int v) => IsZh ? $"{v} 帧" : $"{v} FPS";

    public static string OptVirtual     => IsZh ? "串流逐窗口零回环合成" : "Streamer Per-Window Compositor";
    public static string OptKeepLight   => IsZh ? "白底文档与网页不反黑" : "Keep Light Windows Uninverted";
    public static string OptProtectMedia=> IsZh ? "自动保护照片与视频" : "Protect Photos & Web Videos";
    public static string OptPreserveHue => IsZh ? "保色相反转与文字去彩边" : "OKLab Hue & ClearType De-Fringe";
    public static string OptEnhanceLight=> IsZh ? "浅色区同步去底灰增艳" : "Whiten & Boost Light Regions";
    public static string OptDebugMask   => IsZh ? "显示智能分区调试线框" : "Show Debug Region Overlay";

    public static string FooterShortcuts => IsZh
        ? "快捷键  ·  Win+Alt+I 开关   /   M 切换模式   /   S 呼出面板   /   D 抓取快照"
        : "Hotkeys  ·  Win+Alt+I Toggle   /   M Cycle Mode   /   S Console   /   D Snapshot";

    public static string TrayTooltip => IsZh ? "纸棱镜 — 副屏墨水屏智能折射" : "PaperPrism — Secondary E-Ink Smart Invert";
    public static string TrayStartedTitle => IsZh ? "纸棱镜已启动" : "PaperPrism Started";
    public static string TrayStartedBody  => IsZh ? "副屏智能折射已就绪，按 Win+Alt+S 可呼出面板。" : "E-Ink smart invert active. Press Win+Alt+S to open console.";
    public static string TraySnapTitle    => IsZh ? "已抓取当前画面快照" : "Snapshot Saved";
    public static string TraySnapBody     => IsZh ? "已保存至 snapshots 目录。" : "Diagnostic PNGs exported to snapshots/.";
    public static string TrayMenuSmart    => IsZh ? "智能反色" : "Smart Invert";
    public static string TrayMenuForce    => IsZh ? "强制反色" : "Force Invert";
    public static string TrayMenuCrisp    => IsZh ? "纸感提纯" : "Paper Crisp";
    public static string TrayMenuOff      => IsZh ? "暂停滤镜  ·  Win+Alt+I" : "Pause Filter  ·  Win+Alt+I";
    public static string TrayMenuWhitelist=> IsZh ? "仅对名录内应用开启折射" : "Only Refract Listed Apps";
    public static string TrayMenuRoster   => IsZh ? "登场应用名录..." : "Application Roster...";
    public static string TrayMenuConsole  => IsZh ? "打开控制面板  ·  Win+Alt+S" : "Open Console  ·  Win+Alt+S";
    public static string TrayMenuExit     => IsZh ? "退出程序" : "Exit";
}
