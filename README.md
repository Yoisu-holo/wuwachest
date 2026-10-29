# 鸣潮桌宠 · 宝箱助手

一个独立的 Windows 小工具，用来照看《鸣潮》桌宠 **Wuwa Tappo**：自动领取宝箱奖励、实时显示字符数、记录领取历史，顺带把联机房间和字符统计也管起来。

**不注入、不修改桌宠本体**。它通过 WebView2 的调试端口（CDP）读取页面状态、触发页面上的按钮，读不到时退回 Windows UI Automation，两条路都不碰桌宠的安装文件。

> 这是一个第三方工具，与库洛游戏、Wuwa Tappo 官方无关。使用前请先看文末的[免责声明](#免责声明)。

---

## 功能

### 自动领取
- 读取页面上的宝箱倒计时，**到期后 +2 秒**才动手（早了会被服务端拒绝：`70006 宝箱冷却中`）
- 两个槽位都能领，一次领完后 30 秒复查是否还有
- 字符数不足 1000 时不会瞎点，日志会写「余额不够，按钮被页面隐藏」

### 实时状态（不用轮询）
- 通过 CDP 的 `Runtime.addBinding` 让桌宠页面**主动推送**状态变化：字符数、宝箱、倒计时
- 页面一变，面板立刻更新；连接断了会自动重连并重新挂上

### 内部通道加字符
- 调用桌宠自带的 `cat-panel-action` → `boost-local-clicks` 动作（官方那个隐藏的 +10000 按钮就是它）
- 三档：**+1000 / +5000 / +10000**，瞬间到账，不动鼠标
- 命令行：`WuwaChest.exe --boost 1000`

### 领取记录
- 每次领取写一行日志：时间、字符数变化、消耗、奖励名、截图路径
- 奖励截图**只截弹窗卡片本身**（透明背景、按弹窗矩形精确裁剪），不是拍屏幕
- 记录页可以预览、双击看原图、导出 CSV

### 联机房间
- 实时读取当前房间号和房主
- 自动记录进过的房间（最近 30 个，含访问次数）
- **一键重连**：先离开当前房间，等输入框出现后自动填号、点加入
- 失败会写明原因：格式不对 / 等不到输入框 / 加入按钮一直是灰的 / 等待超时

### 统计图表
- 按小时记录累计字符数，可在「逐小时（24h）」和「逐天（14d）」之间切换
- 柱 = 该时段新增，折线 = 累计

### 其他
- 托盘常驻、单实例、开机自启、领取成功弹通知
- 面板支持自定义背景图（整窗一张图，标签页按窗口坐标对齐）
- 日志按天切割，可配置保留天数

---

## 界面

面板共 5 个标签页：

| 标签页 | 内容 |
| --- | --- |
| 状态 | 字符数、宝箱状态、上次/下次检测、今日领取数、最近日志 |
| 领取记录 | 每天的领取明细（时间、+/-、消耗、奖励、截图） |
| 设置 | 重试间隔、通知、截图、优先 CDP、自动监控、开机自启、背景图 |
| 工具 | 联机房间（当前房间 + 历史 + 重连）、内部通道加字符 |
| 统计 | 字符数曲线（逐小时 / 逐天） |

---

## 环境要求

- Windows 10 / 11
- .NET Framework 4.7 或更高（Win10 1803 以后系统自带）
- 已安装并登录 **Wuwa Tappo** 桌宠

---

## 开启 CDP（推荐）

工具优先用 CDP 读写页面，比 UI Automation 稳得多。开启方式：

**1. 写入 WebView2 启动参数**（管理员 PowerShell）：

```powershell
$key = 'HKLM:\SOFTWARE\Policies\Microsoft\Edge\WebView2\AdditionalBrowserArguments'
New-Item -Path $key -Force | Out-Null
Set-ItemProperty -Path $key -Name 'WuwaTappo.exe' -Value '--remote-debugging-port=9333' -Type String
```

**2. 完全退出桌宠再重新打开**（策略只在启动时生效）。

**3. 验证**：浏览器打开 <http://127.0.0.1:9333/json/version>，能看到一段 JSON 就说明通了。

> 端口可以自己换，工具会自动读注册表里的实际端口，也会在 `127.0.0.1` 和 `localhost` 之间切换（有的机器上 WebView2 只绑 IPv6）。

没开 CDP 也能用，只是会退回 UI Automation：读得慢一些，奖励截图会变成屏幕裁剪（带背景）。

---

## 自动开启 CDP

程序会自己管理调试端口，不需要再手动跑脚本：

**启动时**
1. 检查注册表策略里有没有 `WuwaTappo.exe → --remote-debugging-port=9333`
2. 没有、且当前不是管理员 → 弹 UAC，请求以管理员身份重启自己（拒绝则退回 UIA）
3. 写入策略
4. 如果端口还没开、且桌宠正在运行 → 自动重启桌宠让它加载参数（约 4 秒）
   - 端口本来就开着（桌宠上次就是带参数启动的）→ **不重启**，直接进入 CDP

**退出时**
- 撤销策略（端口在桌宠下次重启后关闭）

**相关配置**

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `cdpAutoEnable` | `true` | 启动时自动检查并写入策略 |
| `cdpAutoRestartPet` | `true` | 写入策略后自动重启桌宠让它生效 |
| `cdpDisableOnExit` | `true` | 退出时撤销策略 |

> 如果不想每次启动都弹一次 UAC，把 `cdpDisableOnExit` 改成 `false`
> （策略保留，之后每次启动都是零打扰、直接 CDP；代价是端口一直开着，仅监听本机）。
---

## 快速开始

1. 下载 `WuwaChest.exe`，放到一个固定目录（例如 `D:\Tools\WuwaChest\`）
2. 双击运行，面板会出现在屏幕中间
3. 打开「设置」，确认「优先用 CDP 读页面」是勾上的
4. 回到「状态」，点「开始监控」

之后它会自己盯着倒计时，到点自动领。关掉窗口会最小化到托盘，右键托盘图标可以退出。

---

## 跨电脑同步

设置页 →「跨电脑同步」→「导出同步包…」/「导入同步包…」。

同步包是一个 `.wcsync` 文件（其实就是 JSON），里面装四样东西：

| 内容 | 来源 |
| --- | --- |
| 设置 | `config.json` |
| 领取记录 | 所有 `logs/chest-*.log` 里的「领取成功」行 |
| 房间历史 | `rooms.txt` |
| 字符数统计 | `stats.txt` |

**合并规则**

| 数据 | 规则 |
| --- | --- |
| 领取记录 | 按日期落到对应日志，**已存在的行不重复添加** |
| 房间 | 同一房间号：时间取较新、次数取较大；新房间直接加入 |
| 字符数 | 同一小时取**较大的累计值**（累计只增不减） |
| 设置 | 导入时选「是」才覆盖本机；选「否」只合并数据 |

导入前的对话框会显示包来自哪台机器、导出时间、各段条数，按「是 / 否 / 取消」决定。

**命令行用法**（适合脚本/自动化）

```text
WuwaChest.exe --sync-export D:\sync\a.wcsync
WuwaChest.exe --sync-import D:\sync\a.wcsync              只合并数据
WuwaChest.exe --sync-import D:\sync\a.wcsync --with-settings   连设置一起覆盖
```
## 配置文件 `config.json`

程序第一次运行会在 exe 同目录生成，改完在「设置」页点「保存并应用」即可。

| 键 | 默认 | 说明 |
| --- | --- | --- |
| `notReadyRetryMinutes` | `5` | 桌宠没开 / 读不到界面时的重试间隔（分钟） |
| `heartbeatSeconds` | `5` | 没有 CDP 推送时的兜底刷新间隔（秒） |
| `verifyDelayMs` | `1200` | 点击后等多久复查状态（毫秒） |
| `notifyOnClaim` | `true` | 领取成功后弹托盘通知 |
| `autoStartMonitor` | `true` | 程序启动后自动开始监控 |
| `logRetentionDays` | `30` | 日志保留天数 |
| `rewardScreenshots` | `true` | 领取时截图记录奖励 |
| `rewardShotDelayMs` | `900` | 点击后等多久截图（毫秒） |
| `backgroundImage` | `""` | 面板背景图（相对 exe 目录或绝对路径） |
| `backgroundDim` | `45` | 背景图压暗程度 0–90 |
| `cdpEnabled` | `true` | 优先用 CDP |
| `cdpPort` | `9333` | 调试端口（和注册表里写的一致） |
| `cdpAutoEnable` | `true` | 启动时自动检查/写入调试策略 |
| `cdpAutoRestartPet` | `true` | 写策略后自动重启桌宠让它生效 |
| `cdpDisableOnExit` | `true` | 退出时撤销「本次自己写的」策略 |

两个固定值不需要配置：**领取一次消耗 1000 字符**、**没有 CDP 时的兜底检测间隔 30 分钟**。

---

## 命令行

```text
WuwaChest.exe                   打开面板（默认启动监控）
WuwaChest.exe --tray            只驻留托盘，不弹窗口
WuwaChest.exe --history         打开面板并切到「领取记录」
WuwaChest.exe --settings        打开面板并切到「设置」
WuwaChest.exe --tools           打开面板并切到「工具」
WuwaChest.exe --stats           打开面板并切到「统计」
WuwaChest.exe --sync-export <文件>              导出同步包
WuwaChest.exe --sync-import <文件>              导入同步包（只合并数据）
WuwaChest.exe --sync-import <文件> --with-settings   连设置一起覆盖
WuwaChest.exe --set-background <图片>           设置面板背景图
WuwaChest.exe --probe           打印识别到的界面元素（排障）
WuwaChest.exe --once            立即检测并领取一次，然后退出
WuwaChest.exe --once --dry-run  只看不点
WuwaChest.exe --boost 1000      走内部通道加 1000 字符（需 CDP）
```

---

## 生成的文件

都在 exe 同目录：

| 路径 | 内容 |
| --- | --- |
| `logs/chest-YYYYMMDD.log` | 每天一个日志 |
| `logs/rewards/*.png` | 奖励截图（透明卡片） |
| `logs/rewards/*.png.txt` | 截图时的元素坐标，排障用 |
| `rooms.txt` | 联机房间历史（`房间号\|房主\|时间\|次数`） |
| `stats.txt` | 每小时字符数（`yyyyMMddHH\|累计值`） |
| `config.json` | 配置 |

这些文件都在 `.gitignore` 里，不会进仓库。

---

## 常见问题

**面板显示「读不到桌宠界面」**
桌宠没启动，或者它以管理员权限运行而本工具没有。两者权限要一致。

**日志写「余额不够 1000，按钮被页面隐藏」**
字符数不足时页面根本不画宝箱按钮，攒够就会自己出现，不用管。

**奖励截图带游戏背景**
说明 CDP 没通、走了屏幕裁剪。检查端口和注册表。

**日志写「CDP 截图全透明（弹窗还没画出来）」**
弹窗有入场动画，工具会自动重试 5 次（每次 400ms）；如果一直失败会退回屏幕裁剪。

**「加入按钮一直是灰的」**
通常是房间号不合法，或者联机本身断开了。

**点击没反应**
确认桌宠窗口不是最小化状态；如果桌宠以管理员运行，本工具也需要管理员权限。

---

## 工作原理

- **页面读写**：通过 WebView2 调试端口连 `CDP`，用 `Runtime.evaluate` 读 Pinia store 和 DOM，用 `Page.captureScreenshot` 截图
- **实时推送**：`Runtime.addBinding` 注册一个 JS 绑定，页面里挂 `MutationObserver`，状态一变就回调
- **兜底**：CDP 不可用时用 Windows UI Automation 找 `Tauri Window` 窗口、读 `Text`、`Invoke` 按钮
- **不碰桌宠文件**：不注入、不打补丁、不改资源，桌宠更新后一般不受影响

---

## 编译

源码是纯 C#，不依赖任何第三方库，用系统自带的 `csc.exe` 就能编：

```powershell
# 直接双击或在 PowerShell 里跑
.\build.ps1
```

`build.ps1` 会调用 `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`，把 `src\*.cs` 编成 `WuwaChest.exe`，并把 `icon.png` 作为资源、`app.ico` 作为程序图标嵌进去。

目录结构：

```
.
├── WuwaChest.exe        编译产物（也可以自己编）
├── build.ps1            一键编译
├── app.manifest         高 DPI / 管理员权限声明
├── app.ico              程序图标
├── icon.png             托盘图标（编译时嵌入）
└── src/
    ├── Program.cs           入口、命令行
    ├── MainForm.cs          面板（5 个标签页）
    ├── ClaimEngine.cs       监控循环、领取、调度
    ├── CdpClient.cs         CDP 会话、页面读写、截图
    ├── CdpWatcher.cs        常驻推送（addBinding + MutationObserver）
    ├── PetBackends.cs       CDP / UIA 两种后端
    ├── PetClient.cs         UI Automation 兜底
    ├── Rooms.cs             联机房间：读取、历史、重连
    ├── Stats.cs             字符数统计
    ├── ChartPanel.cs        图表绘制
    ├── History.cs           领取记录解析
    ├── Screen.cs            屏幕裁剪截图（兜底）
    ├── AppConfig.cs         配置
    └── ...                  日志、托盘、主题控件等
```

---

## 免责声明

- 本工具是第三方软件，与库洛游戏、Wuwa Tappo 官方**没有任何关系**
- 「内部通道加字符」调用的是桌宠程序自带的隐藏动作（官方那个 +10000 按钮），本质上是利用调试接口
- 使用自动化工具**可能违反游戏或桌宠的用户协议**，由此带来的账号风险由使用者自行承担
- 代码按「现状」提供，不对任何损失负责

---

## 更新日志

### v2.2
- 新增：自动开启/关闭 CDP（启动写策略、必要时重启桌宠，退出撤销自己写的）
- 新增：跨电脑同步（导出/导入 `.wcsync`，合并记录、房间、字符数）
- 修复：背景图「先删后拷」导致选到程序目录里的图会把源文件删掉
- 修复：连续两次领取时，第二条记录会串进上一个弹窗的奖励名/文字
- 新增命令行：`--sync-export` / `--sync-import` / `--set-background`

### v2.1
- 修复：联机模式下奖励截图截成「头像栏」——UIA 兜底把玩家列表当成了弹窗
- 弹窗截图窗口延长到约 5 秒，并支持 `.chest-reward-popup-layer` 的挂载方式
- 没有识别到真正的奖励弹窗时不再猜测区域，宁可这次不截图
- 程序内加入版本号（`--help` 可见）

### v2.0
- 重写为独立的 C# 程序，不再依赖 PowerShell
- 优先走 CDP，接不到退回 UI Automation
- 实时推送（不再轮询）、到期驱动调度（倒计时 + 2 秒）
- 奖励截图改成透明卡片（按弹窗矩形精确裁剪）
- 新增：内部通道加字符（+1000 / +5000 / +10000）
- 新增：联机房间记录 + 一键重连
- 新增：字符数统计图表（逐小时 / 逐天）
- 新增：自定义背景图（整窗一张图）
- 去掉：模拟真实点击

---

