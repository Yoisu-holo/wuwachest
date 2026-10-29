// MainForm.cs — 面板：状态 / 领取记录 / 设置，加上托盘与自启
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace WuwaChest
{
    internal sealed class MainForm : Form
    {
        private readonly string _dir;
        private readonly string _configPath;
        private readonly string _logsDir;
        private AppConfig _cfg;
        private readonly ClaimEngine _engine;

        private readonly Label _dot;
        private readonly Label _monitorText;
        private readonly Label _numberText;
        private readonly Label _numberHint;
        private readonly Label _chestBadge;
        private readonly Label _lastCheck;
        private readonly Label _nextCheck;
        private readonly Label _claims;
        private readonly Label _lastClaim;
        private readonly TextBox _logBox;
        private readonly Label _footer;

        private readonly TabControl _tabs;
        private readonly ListView _claimList;
        private readonly Label _claimSummary;
        private readonly PictureBox _claimPreview;
        private readonly Label _claimPreviewHint;

        private readonly NumericUpDown _setNotReady;
        private readonly NumericUpDown _setBackgroundDim;
        private readonly CheckBox _setNotify;
        private readonly CheckBox _setRewardShot;
        private readonly CheckBox _setCdp;
        private readonly CheckBox _setAutostart;
        private readonly CheckBox _setAutoMonitor;

        private readonly Label _farmChars;
        private readonly Label _farmLabel;
        private RoomService _rooms;
        private Label _roomCurrentText;
        private Label _roomStatusText;
        private FlowLayoutPanel _roomRows;
        private bool _roomBusy;
        private StatsStore _stats;
        private ChartPanel _chart;

        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _trayAutostart;
        private readonly System.Windows.Forms.Timer _timer;
        private TrayWatcher _trayWatcher;

        private readonly List<string> _logLines = new List<string>();
        private readonly object _logLock = new object();
        private bool _hiddenToTray;
        private bool _reallyExit;
        private int _claimsToday;
        private string _lastClaimText = "—";
        private string _lastRenderedTail = "";
        private DateTime _claimsLoadedAt = DateTime.MinValue;
        private bool _settingsLoaded;

        private static readonly Color Cream = Color.FromArgb(251, 243, 230);
        private static readonly Color Ink = Color.FromArgb(90, 66, 39);
        private static readonly Color Muted = Color.FromArgb(140, 120, 96);
        private static readonly Color Good = Color.FromArgb(46, 139, 87);
        private static readonly Color Bad = Color.FromArgb(190, 76, 60);
        private static readonly Color Paper = Color.FromArgb(255, 252, 246);

        private static readonly Regex ClaimPattern = new Regex(
            @"领取成功（本次第 \d+ 个）：字符数 (\d+) -> (\d+)", RegexOptions.Compiled);

        public MainForm(AppConfig cfg, bool startHidden, int startTab)
        {
            _cfg = cfg;
            _dir = Application.StartupPath;
            _configPath = Path.Combine(_dir, "config.json");
            _logsDir = Path.Combine(_dir, "logs");
            _engine = new ClaimEngine(cfg, _logsDir);

            Text = "鸣潮桌宠 · 宝箱助手";
            ClientSize = new Size(480, 644);
            MinimumSize = new Size(480, 644);
            MaximumSize = new Size(480, 644);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Cream;
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
            try { Icon = AppIcon.Get(32); } catch { }
            if (Icon == null) { try { Icon = CreateTrayIcon(); } catch { } }

            // ---------------- 状态页 ----------------
            var statusPage = new TabPage("状态") { BackColor = Cream, Padding = new Padding(0) };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                BackColor = Color.Transparent,
                Padding = new Padding(0)
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

            var header = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(120, 82, 45) };
            _dot = new Label
            {
                Text = "●",
                ForeColor = Bad,
                Font = new Font("Microsoft YaHei UI", 11f),
                AutoSize = true,
                Location = new Point(16, 14),
                BackColor = Color.Transparent
            };
            _monitorText = new Label
            {
                Text = "监控：未启动",
                ForeColor = Color.FromArgb(255, 248, 238),
                Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(38, 12),
                BackColor = Color.Transparent
            };
            header.Controls.Add(_dot);
            header.Controls.Add(_monitorText);

            var card = new GlassPanel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Margin = new Padding(14, 8, 14, 4) };
            card.Controls.Add(new Label
            {
                Text = "字符数",
                ForeColor = Muted,
                AutoSize = true,
                Location = new Point(18, 8),
                BackColor = Color.Transparent
            });
            _numberText = new Label
            {
                Text = "—",
                ForeColor = Ink,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 32f, FontStyle.Bold),
                Location = new Point(14, 22),
                BackColor = Color.Transparent
            };
            _numberHint = new Label
            {
                Text = "等待监控进程…",
                ForeColor = Muted,
                AutoSize = false,
                Size = new Size(290, 20),
                Location = new Point(18, 92),
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
            _chestBadge = new Label
            {
                Text = "宝箱：—",
                AutoSize = false,
                Size = new Size(120, 34),
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(330, 40),
                BackColor = Color.FromArgb(238, 228, 212),
                ForeColor = Muted,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold)
            };
            card.Controls.Add(_numberText);
            card.Controls.Add(_numberHint);
            card.Controls.Add(_chestBadge);

            var info = new GlassTable
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 4,
                BackColor = Color.Transparent,
                Margin = new Padding(14, 0, 14, 4),
                Padding = new Padding(14, 4, 14, 4)
            };
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
            info.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) info.RowStyles.Add(new RowStyle(SizeType.Percent, 25));

            info.Controls.Add(MakeKey("状态更新"), 0, 0);
            _lastCheck = MakeValue("—");
            info.Controls.Add(_lastCheck, 1, 0);
            info.Controls.Add(MakeKey("下次检测"), 0, 1);
            _nextCheck = MakeValue("—");
            info.Controls.Add(_nextCheck, 1, 1);
            info.Controls.Add(MakeKey("今日已领"), 0, 2);
            _claims = MakeValue("0 个");
            info.Controls.Add(_claims, 1, 2);
            info.Controls.Add(MakeKey("最近一次"), 0, 3);
            _lastClaim = MakeValue("—");
            info.Controls.Add(_lastClaim, 1, 3);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(14, 4, 14, 4),
                BackColor = Color.Transparent
            };
            buttons.Controls.Add(MakeButton("开始监控", StartMonitor));
            buttons.Controls.Add(MakeButton("停止监控", StopMonitor));
            buttons.Controls.Add(MakeButton("立即检测", RunOnce));
            buttons.Controls.Add(MakeButton("日志目录", OpenLogs));

            var logPanel = new GlassPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(14, 4, 14, 4),
                Padding = new Padding(12, 6, 12, 6)
            };
            logPanel.Controls.Add(new Label
            {
                Text = "最近日志",
                ForeColor = Muted,
                Dock = DockStyle.Top,
                Height = 18,
                BackColor = Color.Transparent
            });
            _logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Paper,
                ForeColor = Color.FromArgb(110, 92, 70),
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 8f)
            };
            logPanel.Controls.Add(_logBox);

            _footer = new Label
            {
                Text = "单文件程序 · 关闭窗口收进托盘 · 退出请用托盘菜单",
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(246, 236, 222),
                Font = new Font("Microsoft YaHei UI", 8f)
            };

            root.Controls.Add(header, 0, 0);
            root.Controls.Add(card, 0, 1);
            root.Controls.Add(info, 0, 2);
            root.Controls.Add(buttons, 0, 3);
            root.Controls.Add(logPanel, 0, 4);
            root.Controls.Add(_footer, 0, 5);
            statusPage.Controls.Add(root);

            // ---------------- 领取记录页 ----------------
            var historyPage = new TabPage("领取记录") { BackColor = Cream, Padding = new Padding(0) };
            var histLayout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                BackColor = Color.Transparent
            };
            histLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            histLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            histLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            histLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            histLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

            _claimSummary = new Label
            {
                Text = "今日 0 个 · 累计 0 个",
                Dock = DockStyle.Fill,
                ForeColor = Ink,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(16, 0, 0, 0),
                BackColor = Paper,
                Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold)
            };

            _claimList = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false,
                BackColor = Paper,
                ForeColor = Ink,
                BorderStyle = BorderStyle.None,
                Font = new Font("Microsoft YaHei UI", 8.5f),
                Margin = new Padding(14, 0, 14, 0)
            };
            _claimList.Columns.Add("时间", 132, HorizontalAlignment.Left);
            _claimList.Columns.Add("字符数变化", 120, HorizontalAlignment.Left);
            _claimList.Columns.Add("消耗", 60, HorizontalAlignment.Left);
            _claimList.Columns.Add("奖励", 100, HorizontalAlignment.Left);
            _claimList.SelectedIndexChanged += delegate { ShowSelectedClaim(); };
            _claimList.DoubleClick += delegate { OpenSelectedClaim(); };

            var histButtons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(14, 4, 14, 4),
                BackColor = Color.Transparent
            };
            histButtons.Controls.Add(MakeButton("刷新记录", delegate { _claimsLoadedAt = DateTime.MinValue; LoadClaims(); }));
            histButtons.Controls.Add(MakeButton("导出 CSV", delegate { ExportClaims(); }));
            histButtons.Controls.Add(MakeButton("日志目录", OpenLogs));

            var previewPanel = new GlassPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Margin = new Padding(14, 0, 14, 4),
                Padding = new Padding(6)
            };
            _claimPreview = new PictureBox
            {
                Dock = DockStyle.Fill,
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Paper,
                Visible = false
            };
            _claimPreviewHint = new Label
            {
                Text = "选中一条记录查看奖励截图，双击打开原图",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Muted,
                BackColor = Color.Transparent
            };
            previewPanel.Controls.Add(_claimPreview);
            previewPanel.Controls.Add(_claimPreviewHint);

            histLayout.Controls.Add(_claimSummary, 0, 0);
            histLayout.Controls.Add(_claimList, 0, 1);
            histLayout.Controls.Add(previewPanel, 0, 2);
            histLayout.Controls.Add(histButtons, 0, 3);
            historyPage.Controls.Add(histLayout);

            // ---------------- 设置页 ----------------
            var settingsPage = new TabPage("设置") { BackColor = Cream, Padding = new Padding(12, 10, 12, 10) };
            var settingsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(16, 8, 16, 8),
                BackColor = Color.Transparent,
                AutoScroll = true
            };

            _setNotReady = MakeNumber(1, 120, 1, 1, _cfg.NotReadyRetryMinutes);
            _setBackgroundDim = MakeNumber(0, 90, 0, 5, _cfg.BackgroundDim);

            _setNotify = MakeCheck("领取成功后弹桌面通知", _cfg.NotifyOnClaim);
            _setRewardShot = MakeCheck("领取时截图记录奖励", _cfg.RewardScreenshots);
            _setCdp = MakeCheck("优先用 CDP 读页面（需已启用调试端口）", _cfg.CdpEnabled);
            _setAutoMonitor = MakeCheck("程序启动后自动开始监控", _cfg.AutoStartMonitor);
            _setAutostart = MakeCheck("开机自动启动（托盘常驻）", Autostart.IsEnabled(Application.ExecutablePath));

            var backgroundButtons = new FlowLayoutPanel
            {
                Width = 216,
                Height = 30,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            backgroundButtons.Controls.Add(MakeButton("选择图片…", delegate { ChooseBackground(); }));
            backgroundButtons.Controls.Add(MakeButton("清除", delegate { ClearBackground(); }));

            settingsFlow.Controls.Add(MakeSettingRow("读不到界面重试（分钟）", _setNotReady));
            settingsFlow.Controls.Add(MakeSettingRow("", _setNotify));
            settingsFlow.Controls.Add(MakeSettingRow("", _setRewardShot));
            settingsFlow.Controls.Add(MakeSettingRow("", _setCdp));
            settingsFlow.Controls.Add(MakeSettingRow("", _setAutoMonitor));
            settingsFlow.Controls.Add(MakeSettingRow("", _setAutostart));
            settingsFlow.Controls.Add(MakeSettingRow("面板背景图", backgroundButtons));
            settingsFlow.Controls.Add(MakeSettingRow("背景图浓度（越大越淡）", _setBackgroundDim));
            var syncButtons = new FlowLayoutPanel
            {
                Width = 236,
                Height = 32,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            syncButtons.Controls.Add(MakeButton("导出同步包…", delegate { ExportSync(); }));
            syncButtons.Controls.Add(MakeButton("导入同步包…", delegate { ImportSync(); }));
            settingsFlow.Controls.Add(MakeSettingRow("跨电脑同步", syncButtons));

            var settingsButtons = new FlowLayoutPanel
            {
                Width = 446,
                Height = 42,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 6, 0, 2)
            };
            settingsButtons.Controls.Add(MakeButton("保存并应用", SaveSettings));
            settingsButtons.Controls.Add(MakeButton("恢复默认", ResetSettings));
            settingsFlow.Controls.Add(settingsButtons);

            settingsFlow.Controls.Add(new Label
            {
                Text = "以上设置会写入 config.json，下次启动继续生效。\r\n高级参数（窗口标题/类名、点击后等待、日志保留天数等）可直接编辑该文件。",
                ForeColor = Muted,
                AutoSize = false,
                Width = 440,
                Height = 72,
                BackColor = Color.Transparent
            });
            var settingsCard = new GlassPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 10, 14, 10)
            };
            settingsCard.Controls.Add(settingsFlow);
            settingsPage.Controls.Add(settingsCard);

            // ---------------- 工具页 ----------------
            var toolsPage = new TabPage("工具") { BackColor = Cream, Padding = new Padding(12, 10, 12, 10) };
            var toolsCard = new GlassPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Transparent,
                Padding = new Padding(14, 12, 14, 12)
            };
            var toolsFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                BackColor = Color.Transparent,
                AutoScroll = true
            };

            BuildRoomSection(toolsFlow);

            toolsFlow.Controls.Add(new Label
            {
                Text = "加字符（内部通道）",
                ForeColor = Ink,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                BackColor = Color.Transparent
            });
            toolsFlow.Controls.Add(new Label
            {
                Text = "通过桌宠自带的内部事件加字符，和官方那个隐藏的 +10000 按钮是同一条通道。\r\n" +
                       "瞬间到账，不动鼠标；服务端会在稍后同步。",
                ForeColor = Muted,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 8)
            });

            _farmChars = new Label
            {
                Text = "当前字符数：—",
                ForeColor = Ink,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8)
            };
            toolsFlow.Controls.Add(_farmChars);


            var boostButtons = new FlowLayoutPanel
            {
                Width = 356,
                Height = 40,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 10)
            };
            foreach (var amount in new[] { 1000, 5000, 10000 })
            {
                int value = amount;
                boostButtons.Controls.Add(MakeButton("+" + value, delegate { BoostClicks(value); }));
            }
            toolsFlow.Controls.Add(boostButtons);

            _farmLabel = new Label
            {
                Text = "空闲",
                ForeColor = Ink,
                AutoSize = false,
                Width = 356,
                Height = 24,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 2, 0, 8)
            };
            toolsFlow.Controls.Add(_farmLabel);

            toolsCard.Controls.Add(toolsFlow);
            toolsPage.Controls.Add(toolsCard);

            // ---------------- 标签页 ----------------
            _tabs = new CompositedTabControl { Dock = DockStyle.Fill, Font = new Font("Microsoft YaHei UI", 9f) };
            _tabs.TabPages.Add(statusPage);
            _tabs.TabPages.Add(historyPage);
            _tabs.TabPages.Add(settingsPage);
            _tabs.TabPages.Add(toolsPage);

            // ---------------- 统计页 ----------------
            var statsPage = new TabPage("统计") { BackColor = Cream, Padding = new Padding(12, 10, 12, 10) };
            var statsHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };

            var modeRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 38,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                BackColor = Color.Transparent,
                Padding = new Padding(0, 0, 0, 6)
            };
            modeRow.Controls.Add(MakeButton("逐小时", delegate { SetChartMode(ChartMode.Hourly); }));
            modeRow.Controls.Add(MakeButton("逐天", delegate { SetChartMode(ChartMode.Daily); }));

            _chart = new ChartPanel { Dock = DockStyle.Fill };
            statsHost.Controls.Add(modeRow);
            statsHost.Controls.Add(_chart);
            statsPage.Controls.Add(statsHost);

            _tabs.TabPages.Add(statsPage);
            if (startTab >= 0 && startTab < _tabs.TabPages.Count) _tabs.SelectedIndex = startTab;
            _tabs.SelectedIndexChanged += delegate
            {
                if (_tabs.SelectedIndex == 1) LoadClaims();
                if (_tabs.SelectedIndex == 2) LoadSettings();
            };
            Controls.Add(_tabs);

            // 减少标签悬停/切换时的闪烁
            EnableDoubleBuffer(this);
            EnableDoubleBuffer(_tabs);
            foreach (TabPage tabPage in _tabs.TabPages) EnableDoubleBuffer(tabPage);

            // ---------------- 托盘 ----------------
            _tray = new NotifyIcon
            {
                Icon = Icon,
                Text = "鸣潮桌宠 · 宝箱助手",
                Visible = true
            };
            var menu = new ContextMenuStrip();
            menu.Items.Add("显示面板", null, delegate { ShowPanel(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("开始监控", null, delegate { StartMonitor(null, EventArgs.Empty); });
            menu.Items.Add("停止监控", null, delegate { StopMonitor(null, EventArgs.Empty); });
            menu.Items.Add("立即检测", null, delegate { RunOnce(null, EventArgs.Empty); });
            menu.Items.Add(new ToolStripSeparator());
            _trayAutostart = new ToolStripMenuItem("开机自启") { CheckOnClick = true };
            _trayAutostart.Checked = Autostart.IsEnabled(Application.ExecutablePath);
            _trayAutostart.Click += delegate
            {
                bool enable = _trayAutostart.Checked;
                ApplyAutostart(enable, false);
                if (_settingsLoaded) _setAutostart.Checked = enable;
            };
            menu.Items.Add(_trayAutostart);
            menu.Items.Add("打开日志目录", null, delegate { OpenLogs(null, EventArgs.Empty); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { _reallyExit = true; Close(); });
            menu.Opening += delegate
            {
                bool enabled = Autostart.IsEnabled(Application.ExecutablePath);
                _trayAutostart.Checked = enabled;
            };
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowPanel(); };
            _tray.BalloonTipClicked += delegate { ShowPanel(); };

            // ---------------- 行为 ----------------
            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized && !_hiddenToTray)
                    BeginInvoke((Action)HideToTray);
            };
            FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                if (_rooms != null) _rooms.Stop();
                if (_stats != null) _stats.Save();
                if (!_reallyExit && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    HideToTray();
                }
            };
            FormClosed += delegate
            {
                _timer.Stop();
                _timer.Dispose();
                if (_trayWatcher != null) { _trayWatcher.DestroyHandle(); _trayWatcher = null; }
                _tray.Visible = false;
                _tray.Dispose();
                try { _engine.Stop(); } catch { }
            };

            _timer = new System.Windows.Forms.Timer { Interval = 1000 };
            _timer.Tick += delegate { RefreshAll(); };
            Load += delegate
            {
                SeedFromLog();
                Log.Written += OnLogWritten;
                _trayWatcher = new TrayWatcher();
                _trayWatcher.TaskbarRestarted += delegate
                {
                    // 资源管理器重启后重新注册托盘图标
                    try { _tray.Visible = false; _tray.Visible = true; } catch { }
                };
                _timer.Start();
                if (_cfg.AutoStartMonitor) _engine.Start();
                ApplyBackground();

            // 联机房间：后台上报当前房间 + 记录历史
            _rooms = new RoomService(_cfg, _dir);
            _rooms.Changed += delegate
            {
                try { if (IsHandleCreated) BeginInvoke((Action)RefreshRoomUi); }
                catch { }
            };
            _rooms.Start();

            _stats = new StatsStore(_dir);
            if (_chart != null) _chart.Bind(_stats);
            RefreshRoomUi();
            EnableDoubleBuffering(this);
                _engine.Peek();
                RefreshAll();
                if (startHidden) BeginInvoke((Action)HideToTray);
            };
        }

        // ------------------------------------------------------------------
        // 小工具
        // ------------------------------------------------------------------
        private static Label MakeKey(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent
            };
        }

        private static Label MakeValue(string text)
        {
            return new Label
            {
                Text = text,
                ForeColor = Ink,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.Transparent,
                AutoEllipsis = true
            };
        }

        private Button MakeButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                Text = text,
                Width = 104,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Paper,
                ForeColor = Ink,
                Margin = new Padding(0, 1, 8, 1),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(214, 198, 174);
            button.Click += onClick;
            return button;
        }

        private static NumericUpDown MakeNumber(decimal min, decimal max, int decimals, decimal step, double value)
        {
            var box = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Increment = step,
                Width = 90,
                Height = 26,
                Font = new Font("Microsoft YaHei UI", 9f)
            };
            decimal v;
            try { v = (decimal)value; }
            catch { v = min; }
            if (v < min) v = min;
            if (v > max) v = max;
            box.Value = v;
            return box;
        }

        private static CheckBox MakeCheck(string text, bool value)
        {
            return new CheckBox
            {
                Text = text,
                Checked = value,
                AutoSize = true,
                ForeColor = Ink,
                Location = new Point(196, 6),
                BackColor = Color.Transparent
            };
        }

        private static Panel MakeSettingRow(string label, Control control)
        {
            var panel = new Panel { Width = 446, Height = 34, BackColor = Color.Transparent };
            if (!string.IsNullOrEmpty(label))
            {
                panel.Controls.Add(new Label
                {
                    Text = label,
                    ForeColor = Ink,
                    AutoSize = false,
                    Width = 190,
                    Height = 26,
                    Location = new Point(0, 6),
                    TextAlign = ContentAlignment.MiddleLeft,
                    BackColor = Color.Transparent
                });
            }
            control.Location = new Point(string.IsNullOrEmpty(label) ? 0 : 196, 4);
            panel.Controls.Add(control);
            return panel;
        }

        // ------------------------------------------------------------------
        // 数据
        // ------------------------------------------------------------------
        private void SeedFromLog()
        {
            var lines = Log.ReadDay(_logsDir, null);
            _claimsToday = 0;
            foreach (var line in lines)
            {
                if (line.IndexOf("领取成功", StringComparison.Ordinal) >= 0) _claimsToday++;
                var m = ClaimPattern.Match(line);
                if (m.Success) _lastClaimText = m.Groups[1].Value + " → " + m.Groups[2].Value;
            }

            lock (_logLock)
            {
                _logLines.Clear();
                int start = Math.Max(0, lines.Count - 200);
                for (int i = start; i < lines.Count; i++) _logLines.Add(lines[i]);
            }
        }

        private void OnLogWritten(LogEntry entry)
        {
            lock (_logLock)
            {
                _logLines.Add(entry.Line);
                while (_logLines.Count > 300) _logLines.RemoveAt(0);
            }

            if (entry.Message.IndexOf("领取成功", StringComparison.Ordinal) >= 0)
            {
                _claimsToday++;
                var m = ClaimPattern.Match(entry.Message);
                if (m.Success)
                {
                    _lastClaimText = m.Groups[1].Value + " → " + m.Groups[2].Value;
                    ShowClaimBalloon(_lastClaimText);
                }
            }
        }

        private void ShowClaimBalloon(string text)
        {
            try
            {
                if (!_cfg.NotifyOnClaim) return;
                _tray.ShowBalloonTip(4000, "宝箱已领取", "字符数 " + text, ToolTipIcon.Info);
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 刷新
        // ------------------------------------------------------------------
        private void RefreshAll()
        {
            var status = _engine.GetStatus();
            bool running = status.Running;
            if (!running) _engine.Peek();
            var pet = status.Pet;

            // 统计：把当前累计字符数记进小时桶
            if (_stats != null && pet != null && pet.Ok && pet.CumulativeClicks > 0)
            {
                if (_stats.Note(pet.CumulativeClicks) && _chart != null) _chart.Invalidate();
            }
            bool fresh = status.PetAt != DateTime.MinValue && (DateTime.Now - status.PetAt).TotalSeconds <= 20;

            if (running)
            {
                SetColor(_dot, Color.FromArgb(126, 217, 87), false);
                SetText(_monitorText, status.Busy ? "监控运行中 · 检测中…" : "监控运行中 · " + status.StatusText);
            }
            else
            {
                SetColor(_dot, Color.FromArgb(230, 120, 100), false);
                SetText(_monitorText, "监控已停止");
            }

            if (fresh && pet != null && pet.Ok)
            {
                SetText(_numberText, pet.Number.ToString("N0", CultureInfo.InvariantCulture));
                SetColor(_numberText, pet.Number >= AppConfig.ClaimCost ? Good : Ink, false);
                SetText(_numberHint, pet.Number >= AppConfig.ClaimCost
                    ? "已够 " + AppConfig.ClaimCost + "，可以领取"
                    : "再攒 " + (AppConfig.ClaimCost - pet.Number).ToString("N0", CultureInfo.InvariantCulture) + " 就能领");

                if (pet.Chest)
                {
                    SetText(_chestBadge, "宝箱：有");
                    SetColor(_chestBadge, Color.FromArgb(215, 238, 222), true);
                    SetColor(_chestBadge, Good, false);
                }
                else
                {
                    SetText(_chestBadge, "宝箱：无");
                    SetColor(_chestBadge, Color.FromArgb(238, 228, 212), true);
                    SetColor(_chestBadge, Muted, false);
                }
            }
            else
            {
                SetText(_numberText, "—");
                SetColor(_numberText, Muted, false);
                SetText(_chestBadge, "宝箱：—");
                SetColor(_chestBadge, Color.FromArgb(238, 228, 212), true);
                SetColor(_chestBadge, Muted, false);

                if (!running) SetText(_numberHint, "监控未运行，点击「开始监控」");
                else if (pet != null && !pet.Ok) SetText(_numberHint, pet.Reason);
                else SetText(_numberHint, "等待监控刷新…");
            }

            SetText(_lastCheck, fresh
                ? (status.LastChangeAt > DateTime.MinValue ? status.LastChangeAt : status.PetAt).ToString("HH:mm:ss") + "（" + pet.Number.ToString("N0", CultureInfo.InvariantCulture) + "）"
                : "—");

            SetText(_nextCheck, running && status.NextRun > DateTime.MinValue
                ? status.NextRun.ToString("HH:mm:ss")
                : "—");

            SetText(_claims, _claimsToday + " 个");
            SetText(_lastClaim, _lastClaimText);

            if (_farmChars != null)
            {
                _farmChars.Text = fresh && pet != null && pet.Ok
                    ? "当前字符数：" + pet.Number.ToString("N0", CultureInfo.InvariantCulture)
                    : "当前字符数：—";
            }

            string tail;
            lock (_logLock)
            {
                int start = Math.Max(0, _logLines.Count - 3);
                var parts = new List<string>();
                for (int i = start; i < _logLines.Count; i++) parts.Add(_logLines[i]);
                tail = parts.Count == 0 ? "（今天还没有日志）" : string.Join("\r\n", parts.ToArray());
            }
            if (tail != _lastRenderedTail)
            {
                _lastRenderedTail = tail;
                _logBox.Text = tail;
                _logBox.SelectionStart = _logBox.TextLength;
                _logBox.ScrollToCaret();
            }

            _footer.Text = string.Format("阈值 {0}（固定）· 实时读取 · 状态更新 {1}",
                AppConfig.ClaimCost,
                fresh ? (status.LastChangeAt > DateTime.MinValue ? status.LastChangeAt : status.PetAt).ToString("HH:mm:ss") : "—");

            try
            {
                string text = "宝箱助手 · " + _claimsToday + " 个 · " + (fresh && pet != null && pet.Ok ? pet.Number.ToString() : "—");
                if (text.Length > 63) text = text.Substring(0, 63);
                _tray.Text = text;
            }
            catch { }

            if (_tabs.SelectedIndex == 1 && (DateTime.Now - _claimsLoadedAt).TotalSeconds >= 10)
                LoadClaims();
        }

        private void LoadClaims()
        {
            List<ClaimRecord> claims;
            try { claims = History.ReadClaims(_logsDir, 500); }
            catch { claims = new List<ClaimRecord>(); }

            _claimsLoadedAt = DateTime.Now;
            _claimList.BeginUpdate();
            try
            {
                _claimList.Items.Clear();
                foreach (var claim in claims)
                {
                    var item = new ListViewItem(claim.When.ToString("MM-dd HH:mm:ss"));
                    item.SubItems.Add(claim.Before.ToString("N0") + " → " + claim.After.ToString("N0"));
                    item.SubItems.Add("-" + AppConfig.ClaimCost.ToString("N0"));
                    item.SubItems.Add(!string.IsNullOrEmpty(claim.Reward) ? claim.Reward
                        : (!string.IsNullOrEmpty(claim.Screenshot) ? "有截图" : "—"));
                    item.Tag = claim;
                    if (claim.When.Date == DateTime.Today) item.ForeColor = Good;
                    _claimList.Items.Add(item);
                }
            }
            finally
            {
                _claimList.EndUpdate();
            }

            int today = 0;
            foreach (var claim in claims)
                if (claim.When.Date == DateTime.Today) today++;

            string last = claims.Count > 0 ? claims[0].When.ToString("MM-dd HH:mm") : "—";
            _claimSummary.Text = string.Format("今日 {0} 个 · 累计 {1} 个 · 最近 {2}", today, claims.Count, last);
        }

        private void ShowSelectedClaim()
        {
            var record = _claimList.SelectedItems.Count > 0 ? _claimList.SelectedItems[0].Tag as ClaimRecord : null;
            string path = ResolveShot(record);

            if (_claimPreview.Image != null)
            {
                var old = _claimPreview.Image;
                _claimPreview.Image = null;
                old.Dispose();
            }

            if (path != null)
            {
                try
                {
                    using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    using (var source = Image.FromStream(stream))
                    {
                        _claimPreview.Image = new Bitmap(source);
                    }
                    _claimPreview.Visible = true;
                    _claimPreviewHint.Visible = false;
                    return;
                }
                catch { }
            }

            _claimPreview.Visible = false;
            _claimPreviewHint.Visible = true;
            _claimPreviewHint.Text = record == null
                ? "选中一条记录查看奖励截图，双击打开原图"
                : "这条记录没有截图";
        }

        private string ResolveShot(ClaimRecord record)
        {
            if (record == null || string.IsNullOrEmpty(record.Screenshot)) return null;
            var path = Path.Combine(_logsDir, record.Screenshot.Replace('/', '\\'));
            return File.Exists(path) ? path : null;
        }

        private void OpenSelectedClaim()
        {
            var record = _claimList.SelectedItems.Count > 0 ? _claimList.SelectedItems[0].Tag as ClaimRecord : null;
            var path = ResolveShot(record);
            if (path == null) return;
            try { Process.Start(path); } catch { }
        }

        // --------------------------------------------------------------
        // 背景图
        // --------------------------------------------------------------
        private void ChooseBackground()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "图片 (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp";
                dialog.Title = "选择面板背景图";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    string stored = BackgroundFile.Install(_dir, dialog.FileName);
                    _cfg = _cfg.Clone();
                    _cfg.BackgroundImage = stored;
                    _cfg.Save(_configPath);
                    _engine.UpdateConfig(_cfg);
                    ApplyBackground();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "设置背景图失败：" + ex.Message, "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void ClearBackground()
        {
            _cfg = _cfg.Clone();
            _cfg.BackgroundImage = "";
            try { _cfg.Save(_configPath); } catch { }
            _engine.UpdateConfig(_cfg);
            ApplyBackground();
        }

        private static void EnableDoubleBuffer(Control control)
        {
            try
            {
                var property = typeof(Control).GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (property != null) property.SetValue(control, true, null);
            }
            catch { }
        }

        private static void SetText(Control control, string value)
        {
            if (control.Text != value) control.Text = value;
        }

        private static void SetColor(Control control, Color color, bool isBackColor)
        {
            if (isBackColor)
            {
                if (control.BackColor != color) control.BackColor = color;
            }
            else
            {
                if (control.ForeColor != color) control.ForeColor = color;
            }
        }

        private static void EnableDoubleBuffering(Control root)
        {
            if (!(root is TextBox) && !(root is ListView)) EnableDoubleBuffer(root);
            foreach (Control child in root.Controls) EnableDoubleBuffering(child);
        }

        private void ApplyBackground()
        {
            BackgroundImage = null;
            foreach (TabPage page in _tabs.TabPages) page.BackgroundImage = null;

            string name = _cfg.BackgroundImage;
            if (string.IsNullOrEmpty(name)) return;

            string path = Path.IsPathRooted(name) ? name : Path.Combine(_dir, name);
            if (!File.Exists(path)) return;

            try
            {
                using (var source = Image.FromFile(path))
                {
                    int width = ClientSize.Width;
                    int height = ClientSize.Height;
                    var bitmap = new Bitmap(width, height);
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                        float scale = Math.Max((float)width / source.Width, (float)height / source.Height);
                        int w = (int)(source.Width * scale) + 1;
                        int h = (int)(source.Height * scale) + 1;
                        g.DrawImage(source, new Rectangle((width - w) / 2, (height - h) / 2, w, h));

                        int dim = Math.Max(0, Math.Min(90, _cfg.BackgroundDim));
                        using (var overlay = new SolidBrush(Color.FromArgb(dim * 255 / 100, Cream)))
                            g.FillRectangle(overlay, 0, 0, width, height);
                    }

                    // 整个窗口只用一张图：每个标签页按它在窗口里的位置裁剪同一张图，
                    // 标签栏和页面拼起来就是连续的一张，不会出现“两张图”。
                    BackgroundImage = bitmap;
                    BackgroundImageLayout = ImageLayout.None;

                    foreach (TabPage page in _tabs.TabPages)
                    {
                        try
                        {
                            Point origin = PointToClient(page.PointToScreen(Point.Empty));
                            int pw = page.ClientSize.Width;
                            int ph = page.ClientSize.Height;
                            if (pw <= 0 || ph <= 0) { page.BackgroundImage = bitmap; continue; }

                            var pageBmp = new Bitmap(pw, ph);
                            using (var pg = Graphics.FromImage(pageBmp))
                            {
                                pg.DrawImage(bitmap, new Rectangle(-origin.X, -origin.Y, width, height));
                            }
                            page.BackgroundImage = pageBmp;
                            page.BackgroundImageLayout = ImageLayout.None;
                            page.BackColor = Cream;
                        }
                        catch { page.BackgroundImage = bitmap; }
                    }
                }
            }
            catch { }
        }

        // ---------------- 联机房间 ----------------
        private void BuildRoomSection(FlowLayoutPanel flow)
        {
            flow.Controls.Add(new Label
            {
                Text = "联机房间",
                ForeColor = Ink,
                AutoSize = true,
                Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold),
                BackColor = Color.Transparent
            });

            _roomCurrentText = new Label
            {
                Text = "当前房间：读取中…",
                ForeColor = Ink,
                AutoSize = true,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 4, 0, 2)
            };
            flow.Controls.Add(_roomCurrentText);

            _roomStatusText = new Label
            {
                Text = "",
                ForeColor = Muted,
                AutoSize = false,
                Width = 356,
                Height = 34,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 4)
            };
            flow.Controls.Add(_roomStatusText);

            _roomRows = new FlowLayoutPanel
            {
                Width = 356,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 8)
            };
            flow.Controls.Add(_roomRows);

            flow.Controls.Add(new Label
            {
                Text = "进过的房间会自动记下来；点「重连」= 先离开当前房间，再自动填号加入，失败会写原因。",
                ForeColor = Muted,
                AutoSize = true,
                MaximumSize = new Size(400, 0),
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 12)
            });
        }

        private void RefreshRoomUi()
        {
            if (_rooms == null || IsDisposed) return;

            var current = _rooms.Current;
            SetText(_roomCurrentText, current.InRoom
                ? "当前房间：" + current.Id + (string.IsNullOrEmpty(current.Owner) ? "" : "    房主 " + current.Owner)
                : "当前房间：不在房间里");

            RebuildRoomRows();
        }

        private void RebuildRoomRows()
        {
            if (_roomRows == null || IsDisposed) return;
            _roomRows.SuspendLayout();
            while (_roomRows.Controls.Count > 0)
            {
                var old = _roomRows.Controls[0];
                _roomRows.Controls.RemoveAt(0);
                old.Dispose();
            }

            var rooms = _rooms.Snapshot();
            string current = _rooms.Current.Id;

            foreach (var entry in rooms)
            {
                var row = new FlowLayoutPanel
                {
                    Width = 356,
                    Height = 34,
                    FlowDirection = FlowDirection.LeftToRight,
                    WrapContents = false,
                    BackColor = Color.Transparent,
                    Margin = new Padding(0, 2, 0, 2)
                };

                string label = entry.Id;
                if (!string.IsNullOrEmpty(entry.Owner)) label += "  " + entry.Owner;
                if (entry.Id == current) label += "（当前）";
                if (entry.Visits > 1) label += "  ×" + entry.Visits;

                var text = new Label
                {
                    Text = label,
                    AutoSize = false,
                    Width = 206,
                    Height = 30,
                    TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = entry.Id == current ? Good : Ink,
                    BackColor = Color.Transparent
                };
                row.Controls.Add(text);

                string roomId = entry.Id;
                var reconnect = MakeButton("重连", delegate { ReconnectRoom(roomId); });
                reconnect.Width = 68;
                row.Controls.Add(reconnect);

                var forget = MakeButton("×", delegate { ForgetRoom(roomId); });
                forget.Width = 40;
                row.Controls.Add(forget);

                _roomRows.Controls.Add(row);
            }

            if (rooms.Count == 0)
            {
                _roomRows.Controls.Add(new Label
                {
                    Text = "还没有记录，进一次房间就有了。",
                    ForeColor = Muted,
                    AutoSize = true,
                    BackColor = Color.Transparent
                });
            }
            _roomRows.ResumeLayout();
        }

        private void ReconnectRoom(string roomId)
        {
            if (_rooms == null || _roomBusy) return;
            _roomBusy = true;
            SetText(_roomStatusText, "正在重连 " + roomId + "…");

            var service = _rooms;
            var thread = new Thread(new ThreadStart(delegate
            {
                string reason;
                bool ok = service.Reconnect(roomId, out reason);
                try
                {
                    BeginInvoke((Action)delegate
                    {
                        _roomBusy = false;
                        if (ok)
                        {
                            SetText(_roomStatusText, "已进入 " + roomId);
                            Log.Action("房间重连成功：" + roomId);
                        }
                        else
                        {
                            SetText(_roomStatusText, "重连失败：" + reason);
                            Log.Warn("房间重连失败（" + roomId + "）：" + reason);
                        }
                        RefreshRoomUi();
                    });
                }
                catch { }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        private void ForgetRoom(string roomId)
        {
            if (_rooms == null) return;
            _rooms.Forget(roomId);
        }

        private void SetChartMode(ChartMode mode)
        {
            if (_chart == null) return;
            _chart.Mode = mode;
        }

        private void ExportClaims()
        {
            List<ClaimRecord> claims;
            try { claims = History.ReadClaims(_logsDir, 5000); }
            catch { claims = new List<ClaimRecord>(); }

            if (claims.Count == 0)
            {
                MessageBox.Show(this, "还没有领取记录。", "导出", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "CSV 文件 (*.csv)|*.csv";
                dialog.FileName = "宝箱领取记录-" + DateTime.Now.ToString("yyyyMMdd") + ".csv";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var sb = new StringBuilder();
                sb.AppendLine("时间,领取前,领取后,消耗");
                foreach (var claim in claims)
                {
                    sb.AppendLine(string.Format("{0:yyyy-MM-dd HH:mm:ss},{1},{2},{3}",
                        claim.When, claim.Before, claim.After, claim.Before - claim.After));
                }
                try
                {
                    File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                    MessageBox.Show(this, "已导出 " + claims.Count + " 条记录。", "导出完成",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ------------------------------------------------------------------
        // 跨电脑同步
        // ------------------------------------------------------------------
        private void ExportSync()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "WuwaChest 同步包 (*" + SyncService.Extension + ")|*" + SyncService.Extension + "|JSON 文件 (*.json)|*.json";
                dialog.FileName = "WuwaChest-" + Environment.MachineName + "-" + DateTime.Now.ToString("yyyyMMdd-HHmm") + SyncService.Extension;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    SyncService.ExportToFile(dialog.FileName, _dir, _logsDir);
                    var pkg = SyncService.Collect(_dir, _logsDir);
                    MessageBox.Show(this,
                        "已导出：\r\n\r\n领取记录 " + pkg.Claims.Count + " 条\r\n房间 " + pkg.Rooms.Count + " 条\r\n字符数 " + pkg.Stats.Count + " 条\r\n设置已包含\r\n\r\n" + dialog.FileName,
                        "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void ImportSync()
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "WuwaChest 同步包 (*" + SyncService.Extension + ")|*" + SyncService.Extension + "|JSON 文件 (*.json)|*.json";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                SyncPackage pkg;
                try { pkg = SyncService.ReadPackage(dialog.FileName); }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "不能读取这个文件", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                var choice = MessageBox.Show(this,
                    "来自：" + pkg.Machine + "    导出时间：" + pkg.ExportedAt + "\r\n" +
                    "领取记录 " + pkg.Claims.Count + " 条 / 房间 " + pkg.Rooms.Count + " 条 / 字符数 " + pkg.Stats.Count + " 条\r\n\r\n" +
                    "「是」= 合并数据，并用包里的设置覆盖本机设置\r\n" +
                    "「否」= 只合并数据，保留本机设置\r\n" +
                    "「取消」= 什么都不做",
                    "导入同步包", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);

                if (choice == DialogResult.Cancel) return;
                bool withSettings = choice == DialogResult.Yes;

                try
                {
                    var result = SyncService.Apply(pkg, _dir, _logsDir, withSettings);

                    // 同步内存里的统计与房间（否则它们下次保存会把合并结果盖掉）
                    if (_stats != null) _stats.MergeLines(pkg.Stats);
                    if (_rooms != null) _rooms.MergeLines(pkg.Rooms);

                    if (result.ConfigApplied)
                    {
                        var reloaded = AppConfig.Load(Path.Combine(_dir, "config.json"));
                        if (reloaded != null) _cfg = reloaded;
                    }

                    LoadClaims();

                    var text = new StringBuilder();
                    text.AppendLine("合并完成：");
                    text.AppendLine("· 领取记录 +" + result.ClaimsAdded + " 条");
                    text.AppendLine("· 房间 " + result.RoomsMerged + " 条");
                    text.AppendLine("· 字符数 " + result.StatsMerged + " 个时段");
                    text.AppendLine(result.ConfigApplied ? "· 设置已用同步包覆盖（部分项重启后生效）" : "· 本机设置保持不变");
                    foreach (var note in result.Notes) text.AppendLine("! " + note);

                    MessageBox.Show(this, text.ToString(), "导入完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }
        // ------------------------------------------------------------------
        // 设置
        // ------------------------------------------------------------------
        private void LoadSettings()
        {
            _settingsLoaded = true;
            _setNotReady.Value = Clamp(_setNotReady, (decimal)_cfg.NotReadyRetryMinutes);
            _setBackgroundDim.Value = Clamp(_setBackgroundDim, (decimal)_cfg.BackgroundDim);
            _setNotify.Checked = _cfg.NotifyOnClaim;
            _setRewardShot.Checked = _cfg.RewardScreenshots;
            _setCdp.Checked = _cfg.CdpEnabled;
            _setAutoMonitor.Checked = _cfg.AutoStartMonitor;
            _setAutostart.Checked = Autostart.IsEnabled(Application.ExecutablePath);
        }

        private static decimal Clamp(NumericUpDown box, decimal value)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }

        private void SaveSettings(object sender, EventArgs e)
        {
            var cfg = _cfg.Clone();
            cfg.NotReadyRetryMinutes = (double)_setNotReady.Value;
            cfg.BackgroundDim = (int)_setBackgroundDim.Value;
            cfg.NotifyOnClaim = _setNotify.Checked;
            cfg.RewardScreenshots = _setRewardShot.Checked;
            cfg.CdpEnabled = _setCdp.Checked;
            cfg.AutoStartMonitor = _setAutoMonitor.Checked;

            try
            {
                cfg.Save(_configPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "保存 config.json 失败：" + ex.Message, "保存失败",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _cfg = cfg;
            _engine.UpdateConfig(cfg);
            ApplyAutostart(_setAutostart.Checked, false);
            ApplyBackground();

            MessageBox.Show(this, "设置已保存并生效。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshAll();
        }

        private void ResetSettings(object sender, EventArgs e)
        {
            var defaults = new AppConfig();
            _setNotReady.Value = (decimal)defaults.NotReadyRetryMinutes;
            _setNotify.Checked = defaults.NotifyOnClaim;
            _setAutoMonitor.Checked = defaults.AutoStartMonitor;
        }

        private void ApplyAutostart(bool enable, bool quiet)
        {
            try
            {
                if (enable) Autostart.Enable(Application.ExecutablePath);
                else Autostart.Disable();

                _trayAutostart.Checked = enable;
                if (_settingsLoaded) _setAutostart.Checked = enable;
                if (!quiet)
                    _tray.ShowBalloonTip(2000, "开机自启",
                        enable ? "已设置为登录后自动启动" : "已取消开机自启", ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                if (!quiet)
                    MessageBox.Show(this, "设置开机自启失败：" + ex.Message, "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ------------------------------------------------------------------
        // 按钮
        // ------------------------------------------------------------------
        private void StartMonitor(object sender, EventArgs e)
        {
            if (_engine.IsRunning) return;
            _engine.Start();
            Log.Action("监控已启动：基础间隔 " + AppConfig.FallbackIntervalMinutes.ToString("0.#", CultureInfo.InvariantCulture) +
                       " 分钟，阈值 " + AppConfig.ClaimCost);
            RefreshAll();
        }

        private void StopMonitor(object sender, EventArgs e)
        {
            if (!_engine.IsRunning) return;
            _engine.Stop();
            Log.Action("监控已停止");
            RefreshAll();
        }

        private void RunOnce(object sender, EventArgs e)
        {
            // 监控没在跑时只跑这一次，不会顺手把持续监控打开
            _engine.CheckNow();
        }

        private void OpenLogs(object sender, EventArgs e)
        {
            try
            {
                if (!Directory.Exists(_logsDir)) Directory.CreateDirectory(_logsDir);
                Process.Start("explorer.exe", "\"" + _logsDir + "\"");
            }
            catch { }
        }


        // 内部通道：瞬间加字符（桌宠自带的 boost-local-clicks）
        private void BoostClicks(int delta)
        {
            if (!CdpDiscovery.IsAvailable(_cfg.CdpPort))
            {
                MessageBox.Show(this,
                    "调试端口没开，内部通道用不了。\r\n\r\n" +
                    "可以先用下面的「真实点击」档位，或者用 运行CDP测试.cmd 启用调试端口。",
                    "内部通道不可用", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(this,
                string.Format("通过桌宠内部通道加 {0} 字符？\r\n\r\n" +
                              "这条通道官方程序里本来就有（隐藏的 +10000 按钮），实测服务端会接受；\r\n" +
                              "但本质仍是利用调试接口，请自行判断风险。", delta),
                "内部通道加字符", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (confirm != DialogResult.OK) return;

            var cdp = new CdpPetClient(_cfg);
            string detail;
            if (cdp.BoostLocalClicks(delta, out detail))
            {
                Log.Action("内部通道加字符：+" + delta);
                _farmLabel.Text = "内部通道已发送 +" + delta + "（服务端会稍后同步）";
                _engine.Peek();
            }
            else
            {
                Log.Warn("内部通道加字符失败：" + detail);
                _farmLabel.Text = "内部通道失败：" + detail;
            }
        }



        // ------------------------------------------------------------------
        // 托盘
        // ------------------------------------------------------------------
        private static Icon CreateTrayIcon()
        {
            var bitmap = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (var bg = new SolidBrush(Color.FromArgb(200, 121, 31)))
                    g.FillEllipse(bg, 1, 1, 30, 30);
                using (var fg = new SolidBrush(Color.FromArgb(255, 248, 236)))
                {
                    g.FillRectangle(fg, 9, 13, 14, 10);
                    g.FillRectangle(fg, 8, 10, 16, 4);
                }
                using (var lockBrush = new SolidBrush(Color.FromArgb(200, 121, 31)))
                    g.FillRectangle(lockBrush, 15, 15, 2, 7);
            }

            IntPtr handle = bitmap.GetHicon();
            try
            {
                using (var temp = Icon.FromHandle(handle)) { return (Icon)temp.Clone(); }
            }
            finally
            {
                DestroyIcon(handle);
                bitmap.Dispose();
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        private void HideToTray()
        {
            _hiddenToTray = true;
            Hide();
            ShowInTaskbar = false;
            try
            {
                _tray.ShowBalloonTip(1500, "宝箱助手", "已最小化到托盘，双击图标可恢复", ToolTipIcon.Info);
            }
            catch { }
        }

        private void ShowPanel()
        {
            _hiddenToTray = false;
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }
    }
}
