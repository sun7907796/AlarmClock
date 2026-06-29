using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace AlarmClockApp
{
    // 以 winmm.dll (MCI) 播放音樂檔，支援 wav / mp3 / wma 等，可自動循環
    public class MciPlayer
    {
        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string cmd, StringBuilder ret, int len, IntPtr cb);

        private static int counter = 0;
        private string alias;
        private bool opened;

        public bool Open(string path)
        {
            alias = "almSnd" + System.Threading.Interlocked.Increment(ref counter);
            int r = mciSendString("open \"" + path + "\" alias " + alias, null, 0, IntPtr.Zero);
            opened = (r == 0);
            return opened;
        }

        public void Play()
        {
            if (opened) mciSendString("play " + alias + " from 0", null, 0, IntPtr.Zero);
        }

        public bool IsPlaying()
        {
            if (!opened) return false;
            var sb = new StringBuilder(64);
            mciSendString("status " + alias + " mode", sb, 64, IntPtr.Zero);
            return sb.ToString().Trim() == "playing";
        }

        public void Close()
        {
            if (!opened) return;
            mciSendString("stop " + alias, null, 0, IntPtr.Zero);
            mciSendString("close " + alias, null, 0, IntPtr.Zero);
            opened = false;
        }
    }

    // 單一鬧鐘資料
    public class Alarm
    {
        public bool Countdown;       // true = 倒數/指定時間
        public int Hour;
        public int Minute;
        public int Second;           // 時鐘模式：指定的秒
        public bool Repeat;          // 時鐘模式：每天重複
        public DateTime Target;      // 倒數模式：下次響鈴時間
        public string Text;
        public string SoundFile;     // 自訂音檔路徑，空=系統音效
        public bool Loop;            // 倒數模式：響完後繼續循環倒數
        public int IntervalSeconds;  // 循環間隔（秒）
        public int StaySeconds;      // 提醒視窗停留秒數，0 = 不自動關閉
        public int Days;             // 重複的星期（bit0=日…bit6=六）；Repeat 且 Days=0 表示每天
        public bool Enabled = true;
        public DateTime? LastFired;  // 時鐘模式避免同一秒重複觸發

        private string StayText()
        {
            if (StaySeconds <= 0) return "停留:手動";
            if (StaySeconds % 60 == 0) return "停留" + (StaySeconds / 60) + "分";
            return "停留" + StaySeconds + "秒";
        }

        public static string DurText(int secs)
        {
            if (secs <= 0) return "0 秒";
            int m = secs / 60, s = secs % 60;
            if (m > 0 && s > 0) return m + " 分 " + s + " 秒";
            if (m > 0) return m + " 分";
            return s + " 秒";
        }

        private static readonly string[] WD = { "日", "一", "二", "三", "四", "五", "六" };

        public static string WeekText(int days)
        {
            var sb = new StringBuilder("週");
            for (int i = 0; i < 7; i++) if ((days & (1 << i)) != 0) sb.Append(WD[i]);
            return sb.ToString();
        }

        // 排序鍵：當天時間（時鐘用設定時間，倒數用目標時間）
        public TimeSpan SortKey()
        {
            return Countdown ? Target.TimeOfDay : new TimeSpan(Hour, Minute, Second);
        }

        public override string ToString()
        {
            string status = Enabled ? "" : "（已停用）";
            string snd = string.IsNullOrEmpty(SoundFile) ? "" : " ♪" + Path.GetFileName(SoundFile);
            if (Countdown)
            {
                string tag = Loop ? string.Format("每{0}循環", DurText(IntervalSeconds)) : "倒數";
                string when = Loop ? string.Format("下次 {0:HH:mm:ss}", Target) : string.Format("{0:MM/dd HH:mm:ss}", Target);
                return string.Format("{0}  [{1}] {2}{3} ({4}) {5}", when, tag, Text, snd, StayText(), status);
            }
            string rep = !Repeat ? "單次" : (Days == 0 ? "每天" : WeekText(Days));
            return string.Format("{0:00}:{1:00}:{2:00}  [{3}] {4}{5} ({6}) {7}",
                Hour, Minute, Second, rep, Text, snd, StayText(), status);
        }

        private static string Esc(string s)
        {
            return (s ?? "").Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
        }

        public string Serialize()
        {
            // 格式 v5： mode|H|M|S|R|TARGET|SOUND|LOOP|INTERVALSEC|STAY|DAYS|TEXT
            string mode = Countdown ? "D" : "C";
            string target = Countdown ? Target.ToString("yyyyMMddHHmmss") : "";
            return string.Join("|", new string[]
            {
                mode, Hour.ToString(), Minute.ToString(), Second.ToString(), Repeat ? "1" : "0",
                target, Esc(SoundFile), Loop ? "1" : "0",
                IntervalSeconds.ToString(), StaySeconds.ToString(), Days.ToString(), Esc(Text)
            });
        }

        public static Alarm Deserialize(string line)
        {
            string[] p = line.Split('|');

            if (p.Length == 4 && IsInt(p[0]))   // 舊格式 v1： H|M|R|Text
            {
                var a1 = new Alarm { SoundFile = "", StaySeconds = 0 };
                int h0, m0, r0;
                int.TryParse(p[0], out h0); a1.Hour = h0;
                int.TryParse(p[1], out m0); a1.Minute = m0;
                int.TryParse(p[2], out r0); a1.Repeat = r0 == 1;
                a1.Text = p[3];
                return a1;
            }

            if (p[0] != "C" && p[0] != "D") return null;
            var a = new Alarm();
            a.Countdown = p[0] == "D";

            // v5： …|STAY|DAYS|TEXT （12 欄）；v4 無 DAYS（11 欄）
            if (p.Length >= 11)
            {
                int h, m, sec, r, lp, iv, st;
                int.TryParse(p[1], out h); a.Hour = h;
                int.TryParse(p[2], out m); a.Minute = m;
                int.TryParse(p[3], out sec); a.Second = sec;
                int.TryParse(p[4], out r); a.Repeat = r == 1;
                if (a.Countdown) a.Target = ParseTarget(p[5]);
                a.SoundFile = p[6];
                int.TryParse(p[7], out lp); a.Loop = lp == 1;
                int.TryParse(p[8], out iv); a.IntervalSeconds = iv;
                int.TryParse(p[9], out st); a.StaySeconds = st;
                if (p.Length >= 12)
                {
                    int dd; int.TryParse(p[10], out dd); a.Days = dd;
                    a.Text = p[11];
                }
                else a.Text = p[10];
                return a;
            }

            // 舊 v2/v3： mode|H|M|R|TARGET|SOUND|...（無秒；INTERVAL 為分鐘）
            if (p.Length >= 7)
            {
                int h, m, r;
                int.TryParse(p[1], out h); a.Hour = h;
                int.TryParse(p[2], out m); a.Minute = m;
                int.TryParse(p[3], out r); a.Repeat = r == 1;
                if (a.Countdown) a.Target = ParseTarget(p[4]);
                a.SoundFile = p[5];
                if (p.Length >= 10)   // v3
                {
                    int lp, ivMin, st;
                    int.TryParse(p[6], out lp); a.Loop = lp == 1;
                    int.TryParse(p[7], out ivMin); a.IntervalSeconds = ivMin * 60;   // 分→秒
                    int.TryParse(p[8], out st); a.StaySeconds = st;
                    a.Text = p[9];
                }
                else a.Text = p[6];   // v2
                return a;
            }
            return null;
        }

        private static DateTime ParseTarget(string s)
        {
            DateTime t;
            DateTime.TryParseExact(s, "yyyyMMddHHmmss", null,
                System.Globalization.DateTimeStyles.None, out t);
            return t;
        }

        private static bool IsInt(string s) { int x; return int.TryParse(s, out x); }
    }

    public class MainForm : Form
    {
        private DateTimePicker timePicker;
        private Button btnNow;
        private CheckBox chkRepeat;
        private readonly CheckBox[] chkDays = new CheckBox[7];
        private Button btnAdd;
        private NumericUpDown numCdMin;
        private NumericUpDown numCdSec;
        private CheckBox chkLoop;
        private Button btnCountdown;
        private NumericUpDown numStay;
        private ComboBox cboStayUnit;
        private CheckBox chkNoAuto;
        private ComboBox cboReminder;
        private TextBox txtSound;
        private Button btnBrowse;
        private Button btnTest;
        private Button btnClearSound;
        private Button btnTestPopup;
        private ComboBox cboSort;
        private ListBox lstAlarms;
        private Button btnDelete;
        private Button btnToggle;
        private CheckBox chkMasterStop;
        private Label lblClock;

        // 配色
        private static readonly Color Accent = Color.FromArgb(63, 81, 181);
        private static readonly Color FormBg = Color.FromArgb(245, 246, 250);
        private Timer timer;
        private NotifyIcon tray;
        private MciPlayer testPlayer;
        private string soundPath = "";
        private bool paused;
        private bool loadingSettings;

        // 預設鈴聲（放在 exe 同資料夾，沒有自選音樂時使用）
        public static readonly string DefaultSoundFile =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "10 second short music_320k.mp3");

        private readonly List<Alarm> alarms = new List<Alarm>();
        private readonly string savePath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alarms.txt");
        private readonly string settingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.txt");
        private readonly string remindersPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reminders.txt");
        private GroupBox grpClock;
        private GroupBox grpCountdown;
        private Button btnAddPreset;
        private Button btnDelPreset;
        private Alarm editing;   // 正在編輯的鬧鐘（null = 新增模式）
        private System.Threading.EventWaitHandle showEvent;   // 第二次啟動時用來通知本視窗顯示

        // LINE 群組通知設定
        private bool lineOn;
        private string lineToken = "";
        private string lineTo = "";
        private Button btnLine;

        // 動態UI自訂圖片（空=用預設時鐘造型）
        private string uiImagePath = "";
        private TextBox txtUiImage;
        private Button btnUiImage;
        private Button btnUiReset;
        private Button btnHelp;

        public MainForm()
        {
            BuildUi();
            LoadAlarms();
            NormalizeOnStartup();   // 避免開啟時立刻觸發過期的倒數鬧鐘
            RefreshList();
            LoadSettings();   // 還原暫停狀態
            StartShowListener();
            timer = new Timer { Interval = 1000 };
            timer.Tick += Timer_Tick;
            timer.Start();
        }

        // 啟動時把「已過期」的倒數鬧鐘往後重排（循環）或停用（單次），避免一開程式就立刻彈出
        private void NormalizeOnStartup()
        {
            DateTime now = DateTime.Now;
            bool changed = false;
            foreach (var a in alarms)
            {
                if (a.Countdown && a.Enabled && now >= a.Target)
                {
                    if (a.Loop && a.IntervalSeconds > 0) a.Target = now.AddSeconds(a.IntervalSeconds);
                    else a.Enabled = false;
                    changed = true;
                }
            }
            if (changed) SaveAlarms();
        }

        // 使用說明對話框
        private void ShowHelpDialog()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "桌面鬧鐘 - 使用說明";
                dlg.ClientSize = new Size(520, 560);
                dlg.FormBorderStyle = FormBorderStyle.Sizable;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MinimizeBox = false;
                dlg.BackColor = FormBg;
                dlg.Font = new Font("Microsoft JhengHei", 9.5F);
                try { dlg.Icon = Icon; } catch { }

                var txt = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                    Left = 12, Top = 12, Width = 496, Height = 496,
                    Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                    BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White,
                    Font = new Font("Microsoft JhengHei", 10.5F), Text = HelpText()
                };
                txt.Select(0, 0);
                var btnClose = new Button
                {
                    Text = "關閉", Left = 408, Top = 518, Width = 100, Height = 30,
                    Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK
                };
                dlg.Controls.Add(txt);
                dlg.Controls.Add(btnClose);
                dlg.AcceptButton = btnClose;
                dlg.ShowDialog(this);
            }
        }

        private static string HelpText()
        {
            return string.Join("\r\n", new[]
            {
                "【桌面鬧鐘 使用說明】",
                "",
                "■ 指定時間鬧鐘",
                "  1) 設定「鬧鐘時間」(時:分:秒)，可按「現在」帶入目前時間。",
                "  2) 勾「重複」後可選星期幾(不選=每天)；不勾為單次。",
                "  3) 輸入提醒項目，按「＋ 新增鬧鐘」。",
                "  ※ 同一時間不可重複新增。",
                "",
                "■ 倒數計時 / 循環",
                "  1) 在「倒數間隔」填 分、秒。",
                "  2) 勾「循環倒數」= 響完後每隔該時間再響(例：每30分起身)。",
                "  3) 按「＋ 倒數新增」。",
                "",
                "■ 提醒項目(下拉選單)",
                "  ‧ 可直接輸入文字。",
                "  ‧ ＋：把目前文字加入選單；－：刪除目前選項。",
                "",
                "■ 響鈴音樂",
                "  ‧ 預設使用內建鈴聲；可「瀏覽」選 wav/mp3/wma 檔。",
                "  ‧ 「試聽」測試、「恢復預設鈴聲」還原。",
                "",
                "■ 動態UI停留時間",
                "  ‧ 鬧鐘響時，時鐘角色會在螢幕下方走動，停留時間到後定點停住、再自動關閉。",
                "  ‧ 勾「不自動關閉」則需手動關閉。",
                "  ‧ 按「測試動態UI」可立即預覽效果。",
                "",
                "■ 鬧鐘清單",
                "  ‧ 雙擊項目可載入上方表單編輯，改完按「✓ 更新」。",
                "  ‧ 「排序」可依時間早→晚 / 晚→早排列。",
                "  ‧ 「刪除選取」「啟用/停用」管理項目；停用或暫停中的項目顯示為灰色。",
                "",
                "■ 暫停所有鬧鐘",
                "  ‧ 勾選後所有鬧鐘暫停不執行；取消勾選恢復(倒數會重新計時)。",
                "",
                "■ LINE 通知(選用)",
                "  ‧ 「LINE 通知設定…」填入 Channel access token 與目標 ID。",
                "  ‧ 目標 ID：群組為 C 開頭、個人為 U 開頭(需先把 Bot 加為好友/邀入群組)。",
                "  ‧ 啟用後，鬧鐘響時會同步推播到 LINE(桌面彈窗照常)。",
                "",
                "■ 其他",
                "  ‧ 關閉視窗會縮到右下角系統匣，仍在背景提醒；要完全結束請對系統匣圖示按右鍵→結束程式。",
                "  ‧ 程式僅允許開啟一個，重複開啟會把既有視窗叫回前景。",
                "  ‧ 設定與鬧鐘會自動儲存，下次開啟自動載入。"
            });
        }

        // 監聽「再次啟動」訊號：有人重複開啟 exe 時，把本視窗叫回前景（即使縮在系統匣）
        private void StartShowListener()
        {
            try
            {
                bool created;
                showEvent = new System.Threading.EventWaitHandle(
                    false, System.Threading.EventResetMode.AutoReset, Program.ShowEventName, out created);
            }
            catch { return; }

            var t = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    try { showEvent.WaitOne(); } catch { break; }
                    try
                    {
                        if (IsDisposed) break;
                        BeginInvoke((Action)ShowFromTray);
                    }
                    catch { break; }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void BuildUi()
        {
            Text = "桌面鬧鐘";
            ClientSize = new Size(548, 750);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft JhengHei", 9F);
            BackColor = FormBg;

            Icon appIcon = null;
            try { appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            if (appIcon != null) Icon = appIcon;

            // ── 頂部標題列 ──
            var header = new Panel { Dock = DockStyle.Top, Height = 58, BackColor = Accent };
            var lblTitle = new Label
            {
                Text = "⏰ 桌面鬧鐘", ForeColor = Color.White,
                Font = new Font("Microsoft JhengHei", 14F, FontStyle.Bold),
                AutoSize = true, Left = 16, Top = 14, BackColor = Color.Transparent
            };
            lblClock = new Label
            {
                Font = new Font("Consolas", 15F, FontStyle.Bold), ForeColor = Color.White,
                TextAlign = ContentAlignment.MiddleRight, BackColor = Color.Transparent,
                Left = 240, Top = 8, Width = 296, Height = 42
            };
            header.Controls.Add(lblTitle);
            header.Controls.Add(lblClock);

            var normal = new Font("Microsoft JhengHei", 9F);

            // ── 共用設定（提醒內容 / 鈴聲 / 停留時間）──
            var grpCommon = new GroupBox
            {
                Text = "提醒內容與鈴聲（共用）", Left = 12, Top = 66, Width = 524, Height = 184,
                BackColor = Color.White, Font = new Font("Microsoft JhengHei", 9F, FontStyle.Bold),
                ForeColor = Accent
            };
            // 提醒項目（下拉，可自行輸入）
            grpCommon.Controls.Add(new Label { Text = "提醒項目：", Left = 12, Top = 28, Width = 76, Font = normal, ForeColor = Color.Black });
            cboReminder = new ComboBox { Left = 90, Top = 25, Width = 352, DropDownStyle = ComboBoxStyle.DropDown, Font = normal };
            LoadReminders();   // 從 reminders.txt 載入選項（無檔時用預設並建立）
            btnAddPreset = new Button { Text = "＋", Left = 446, Top = 24, Width = 28, Height = 26 };
            btnAddPreset.Click += (s, e) => AddPreset();
            btnDelPreset = new Button { Text = "－", Left = 478, Top = 24, Width = 28, Height = 26 };
            btnDelPreset.Click += (s, e) => DeletePreset();
            grpCommon.Controls.Add(cboReminder);
            grpCommon.Controls.Add(btnAddPreset);
            grpCommon.Controls.Add(btnDelPreset);
            // 響鈴音樂
            grpCommon.Controls.Add(new Label { Text = "響鈴音樂：", Left = 12, Top = 60, Width = 76, Font = normal, ForeColor = Color.Black });
            soundPath = File.Exists(DefaultSoundFile) ? DefaultSoundFile : "";
            txtSound = new TextBox { Left = 90, Top = 57, Width = 330, ReadOnly = true, Text = SoundLabel(), Font = normal };
            btnBrowse = new Button { Text = "瀏覽…", Left = 426, Top = 55, Width = 80, Height = 26 };
            btnBrowse.Click += (s, e) => BrowseSound();
            grpCommon.Controls.Add(txtSound);
            grpCommon.Controls.Add(btnBrowse);
            // 音樂相關 + 測試動態
            btnTest = new Button { Text = "試聽", Left = 90, Top = 88, Width = 88, Height = 26 };
            btnTest.Click += (s, e) => TestSound();
            btnClearSound = new Button { Text = "恢復預設鈴聲", Left = 184, Top = 88, Width = 110, Height = 26 };
            btnClearSound.Click += (s, e) =>
            {
                soundPath = File.Exists(DefaultSoundFile) ? DefaultSoundFile : "";
                txtSound.Text = SoundLabel(); StopTest();
            };
            btnTestPopup = new Button { Text = "測試動態UI", Left = 300, Top = 88, Width = 96, Height = 26 };
            btnTestPopup.Click += (s, e) =>
                new AlarmPopup(CurrentText(), DateTime.Now.ToString("HH:mm:ss"), soundPath, CurrentStaySeconds(), uiImagePath).Show();
            grpCommon.Controls.Add(btnTest);
            grpCommon.Controls.Add(btnClearSound);
            grpCommon.Controls.Add(btnTestPopup);
            // 動態UI停留時間
            grpCommon.Controls.Add(new Label { Text = "停留時間：", Left = 12, Top = 122, Width = 76, Font = normal, ForeColor = Color.Black });
            numStay = new NumericUpDown { Left = 90, Top = 119, Width = 56, Minimum = 1, Maximum = 999, Value = 3, Font = normal };
            cboStayUnit = new ComboBox { Left = 150, Top = 119, Width = 56, DropDownStyle = ComboBoxStyle.DropDownList, Font = normal };
            cboStayUnit.Items.AddRange(new object[] { "分", "秒" });
            cboStayUnit.SelectedIndex = 0;
            chkNoAuto = new CheckBox { Text = "不自動關閉（手動）", Left = 214, Top = 121, Width = 180, Font = normal, ForeColor = Color.Black };
            chkNoAuto.CheckedChanged += (s, e) => { numStay.Enabled = !chkNoAuto.Checked; cboStayUnit.Enabled = !chkNoAuto.Checked; };
            grpCommon.Controls.Add(numStay);
            grpCommon.Controls.Add(cboStayUnit);
            grpCommon.Controls.Add(chkNoAuto);
            // 動態UI圖片（自訂角色圖；空=預設時鐘造型）
            grpCommon.Controls.Add(new Label { Text = "動態圖片：", Left = 12, Top = 154, Width = 76, Font = normal, ForeColor = Color.Black });
            txtUiImage = new TextBox { Left = 90, Top = 151, Width = 276, ReadOnly = true, Text = UiImageLabel(), Font = normal };
            btnUiImage = new Button { Text = "瀏覽…", Left = 372, Top = 150, Width = 64, Height = 26 };
            btnUiImage.Click += (s, e) => BrowseUiImage();
            btnUiReset = new Button { Text = "用預設", Left = 442, Top = 150, Width = 64, Height = 26 };
            btnUiReset.Click += (s, e) => { uiImagePath = ""; txtUiImage.Text = UiImageLabel(); SaveSettings(); };
            grpCommon.Controls.Add(txtUiImage);
            grpCommon.Controls.Add(btnUiImage);
            grpCommon.Controls.Add(btnUiReset);

            // ── 指定時間鬧鐘 ──
            grpClock = new GroupBox
            {
                Text = "指定時間鬧鐘", Left = 12, Top = 258, Width = 524, Height = 122,
                BackColor = Color.White, Font = new Font("Microsoft JhengHei", 9F, FontStyle.Bold),
                ForeColor = Accent
            };
            grpClock.Controls.Add(new Label { Text = "鬧鐘時間：", Left = 12, Top = 28, Width = 76, Font = normal, ForeColor = Color.Black });
            timePicker = new DateTimePicker
            {
                Left = 90, Top = 25, Width = 100, Font = normal,
                Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm:ss", ShowUpDown = true
            };
            btnNow = new Button { Text = "現在時間", Left = 196, Top = 24, Width = 60, Height = 26 };
            btnNow.Click += (s, e) => timePicker.Value = DateTime.Now;
            chkRepeat = new CheckBox { Text = "重複", Left = 262, Top = 27, Width = 56, Font = normal, ForeColor = Color.Black };
            chkRepeat.CheckedChanged += (s, e) => UpdateDayBoxes();
            grpClock.Controls.Add(timePicker);
            grpClock.Controls.Add(btnNow);
            grpClock.Controls.Add(chkRepeat);
            grpClock.Controls.Add(new Label { Text = "重複星期：", Left = 12, Top = 58, Width = 76, Font = normal, ForeColor = Color.Black });
            for (int i = 0; i < 7; i++)
            {
                chkDays[i] = new CheckBox
                {
                    Text = new[] { "日", "一", "二", "三", "四", "五", "六" }[i],
                    Left = 90 + i * 60, Top = 57, Width = 56, Font = normal, ForeColor = Color.Black, Enabled = false
                };
                grpClock.Controls.Add(chkDays[i]);
            }
            btnAdd = new Button { Text = "＋ 新增鬧鐘", Left = 90, Top = 86, Width = 416, Height = 30 };
            btnAdd.Click += (s, e) =>
            {
                if (editing == null) AddClockAlarm();
                else if (editing.Countdown) ExitEdit();   // 編輯倒數時此鈕為「取消編輯」
                else UpdateEditing();                       // 編輯時鐘時此鈕為「✓ 更新」
            };
            grpClock.Controls.Add(btnAdd);

            // ── 倒數計時 ──
            grpCountdown = new GroupBox
            {
                Text = "倒數計時", Left = 12, Top = 388, Width = 524, Height = 94,
                BackColor = Color.White, Font = new Font("Microsoft JhengHei", 9F, FontStyle.Bold),
                ForeColor = Accent
            };
            grpCountdown.Controls.Add(new Label { Text = "倒數間隔：", Left = 12, Top = 28, Width = 76, Font = normal, ForeColor = Color.Black });
            numCdMin = new NumericUpDown { Left = 90, Top = 25, Width = 46, Minimum = 0, Maximum = 1440, Value = 30, Font = normal };
            grpCountdown.Controls.Add(new Label { Text = "分", Left = 138, Top = 28, Width = 16, Font = normal, ForeColor = Color.Black });
            numCdSec = new NumericUpDown { Left = 156, Top = 25, Width = 46, Minimum = 0, Maximum = 59, Value = 0, Font = normal };
            grpCountdown.Controls.Add(new Label { Text = "秒", Left = 204, Top = 28, Width = 16, Font = normal, ForeColor = Color.Black });
            chkLoop = new CheckBox { Text = "循環倒數", Left = 226, Top = 27, Width = 100, Font = normal, ForeColor = Color.Black };
            grpCountdown.Controls.Add(numCdMin);
            grpCountdown.Controls.Add(numCdSec);
            grpCountdown.Controls.Add(chkLoop);
            btnCountdown = new Button { Text = "＋ 倒數新增", Left = 90, Top = 56, Width = 416, Height = 30 };
            btnCountdown.Click += (s, e) =>
            {
                if (editing == null) AddCountdownAlarm();
                else if (editing.Countdown) UpdateEditing();   // 編輯倒數時此鈕為「✓ 更新」
                else ExitEdit();                                // 編輯時鐘時此鈕為「取消編輯」
            };
            grpCountdown.Controls.Add(btnCountdown);

            // ── 鬧鐘清單 群組 ──
            var grpList = new GroupBox
            {
                Text = "鬧鐘清單", Left = 12, Top = 490, Width = 524, Height = 252,
                BackColor = Color.White, Font = new Font("Microsoft JhengHei", 9F, FontStyle.Bold),
                ForeColor = Accent
            };
            grpList.Controls.Add(new Label { Text = "排序：", Left = 12, Top = 26, Width = 44, Font = normal, ForeColor = Color.Black });
            cboSort = new ComboBox { Left = 56, Top = 23, Width = 150, DropDownStyle = ComboBoxStyle.DropDownList, Font = normal };
            cboSort.Items.AddRange(new object[] { "新增順序", "時間：早→晚 ↑", "時間：晚→早 ↓" });
            cboSort.SelectedIndex = 0;
            cboSort.SelectedIndexChanged += (s, e) => RefreshList();
            grpList.Controls.Add(cboSort);
            grpList.Controls.Add(new Label { Text = "（雙擊項目可編輯）", Left = 214, Top = 26, Width = 130, Font = normal, ForeColor = Color.Gray });
            btnLine = new Button { Text = "LINE 通知設定…", Left = 360, Top = 21, Width = 152, Height = 28 };
            btnLine.Click += (s, e) => ShowLineDialog();
            grpList.Controls.Add(btnLine);

            lstAlarms = new ListBox
            {
                Left = 12, Top = 52, Width = 500, Height = 134,
                Font = normal, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 24,
                BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false
            };
            lstAlarms.DrawItem += LstAlarms_DrawItem;
            lstAlarms.DoubleClick += (s, e) => { var a = lstAlarms.SelectedItem as Alarm; if (a != null) EnterEdit(a); };
            grpList.Controls.Add(lstAlarms);

            btnDelete = new Button { Text = "刪除選取", Left = 12, Top = 194, Width = 96, Height = 34 };
            btnDelete.Click += (s, e) => DeleteSelected();
            btnToggle = new Button { Text = "啟用/停用", Left = 114, Top = 194, Width = 96, Height = 34 };
            btnToggle.Click += (s, e) => ToggleSelected();
            btnHelp = new Button { Text = "使用說明", Left = 216, Top = 194, Width = 96, Height = 34 };
            btnHelp.Click += (s, e) => ShowHelpDialog();
            grpList.Controls.Add(btnHelp);
            chkMasterStop = new CheckBox
            {
                Text = "⏸ 暫停所有鬧鐘", Left = 318, Top = 194, Width = 194, Height = 34,
                Font = new Font("Microsoft JhengHei", 10F, FontStyle.Bold),
                Appearance = Appearance.Button, TextAlign = ContentAlignment.MiddleCenter,
                FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.Firebrick,
                Cursor = Cursors.Hand
            };
            chkMasterStop.FlatAppearance.BorderSize = 0;
            chkMasterStop.CheckedChanged += (s, e) => OnMasterStopChanged();
            chkMasterStop.CheckedChanged += (s, e) => chkMasterStop.Invalidate();
            bool hovM = false;
            chkMasterStop.MouseEnter += (s, e) => { hovM = true; chkMasterStop.Invalidate(); };
            chkMasterStop.MouseLeave += (s, e) => { hovM = false; chkMasterStop.Invalidate(); };
            chkMasterStop.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = chkMasterStop.ClientRectangle; rect.Width -= 1; rect.Height -= 1;
                Color fill = chkMasterStop.Checked ? Color.FromArgb(255, 205, 205)
                    : (hovM ? Color.FromArgb(255, 224, 225) : Color.FromArgb(255, 235, 236));
                using (var path = RoundedPath(rect, 9))
                using (var br = new SolidBrush(fill))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, chkMasterStop.Text, chkMasterStop.Font, chkMasterStop.ClientRectangle,
                    Color.Firebrick, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            grpList.Controls.Add(btnDelete);
            grpList.Controls.Add(btnToggle);
            grpList.Controls.Add(chkMasterStop);

            Controls.Add(grpCommon);
            Controls.Add(grpClock);
            Controls.Add(grpCountdown);
            Controls.Add(grpList);
            Controls.Add(header);

            // 統一按鈕樣式
            StyleButton(btnAdd, true);
            StyleButton(btnCountdown, true);
            foreach (var b in new[] { btnNow, btnBrowse, btnTest, btnClearSound, btnTestPopup, btnDelete, btnToggle, btnAddPreset, btnDelPreset, btnLine, btnHelp, btnUiImage, btnUiReset })
                StyleButton(b, false);

            tray = new NotifyIcon
            {
                Icon = appIcon != null ? appIcon : SystemIcons.Application,
                Text = "桌面鬧鐘（執行中）", Visible = true
            };
            var menu = new ContextMenu();
            menu.MenuItems.Add("顯示主視窗", (s, e) => ShowFromTray());
            menu.MenuItems.Add("結束程式", (s, e) => { tray.Visible = false; Application.Exit(); });
            tray.ContextMenu = menu;
            tray.DoubleClick += (s, e) => ShowFromTray();

            FormClosing += MainForm_FormClosing;
        }

        // 圓角矩形路徑（用於反鋸齒自繪，邊緣平滑無毛邊）
        private static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            int d = radius * 2;
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            var p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        // 自繪圓角按鈕：底色填白（與卡片融合）+ 反鋸齒圓角，邊緣不再有毛邊
        private static void StyleButton(Button b, bool primary)
        {
            Color fill = primary ? Accent : Color.FromArgb(233, 236, 243);
            Color hover = primary ? Color.FromArgb(92, 107, 192) : Color.FromArgb(220, 224, 235);
            Color down = primary ? Color.FromArgb(48, 63, 159) : Color.FromArgb(206, 211, 223);
            Color fg = primary ? Color.White : Color.FromArgb(55, 58, 70);

            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Color.White;     // 四角填白，與白色群組卡片融合
            b.ForeColor = fg;
            b.Font = new Font("Microsoft JhengHei", primary ? 10F : 9F, primary ? FontStyle.Bold : FontStyle.Regular);
            b.Cursor = Cursors.Hand;
            b.Region = null;

            bool hov = false, dn = false;
            b.MouseEnter += (s, e) => { hov = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { hov = false; dn = false; b.Invalidate(); };
            b.MouseDown += (s, e) => { dn = true; b.Invalidate(); };
            b.MouseUp += (s, e) => { dn = false; b.Invalidate(); };
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = b.ClientRectangle; rect.Width -= 1; rect.Height -= 1;
                Color c = dn ? down : (hov ? hover : fill);
                using (var path = RoundedPath(rect, 9))
                using (var br = new SolidBrush(c))
                    g.FillPath(br, path);
                TextRenderer.DrawText(g, b.Text, b.Font, b.ClientRectangle, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            };
        }

        private void UpdateDayBoxes()
        {
            foreach (var c in chkDays) { c.Enabled = chkRepeat.Checked; if (!chkRepeat.Checked) c.Checked = false; }
        }

        // ---- 提醒項目下拉選單：自行新增 / 刪除 ----
        private void LoadReminders()
        {
            cboReminder.Items.Clear();
            string[] defaults = { "起身活動！", "喝水休息一下", "起來走一走", "該吃藥了",
                                  "準備開會", "讓眼睛休息一下", "下班囉！", "深呼吸放鬆一下" };
            try
            {
                if (File.Exists(remindersPath))
                    foreach (var line in File.ReadAllLines(remindersPath))
                    {
                        var t = line.Trim();
                        if (t.Length > 0 && !cboReminder.Items.Contains(t)) cboReminder.Items.Add(t);
                    }
            }
            catch { }
            if (cboReminder.Items.Count == 0) { cboReminder.Items.AddRange(defaults); SaveReminders(); }
            cboReminder.Text = cboReminder.Items[0].ToString();
        }

        private void SaveReminders()
        {
            try
            {
                var list = new List<string>();
                foreach (var it in cboReminder.Items) list.Add(it.ToString());
                File.WriteAllLines(remindersPath, list.ToArray());
            }
            catch { }
        }

        private void AddPreset()
        {
            string t = (cboReminder.Text ?? "").Trim();
            if (t.Length == 0) return;
            if (!cboReminder.Items.Contains(t)) { cboReminder.Items.Add(t); SaveReminders(); }
            cboReminder.Text = t;
        }

        private void DeletePreset()
        {
            string t = (cboReminder.Text ?? "").Trim();
            int idx = cboReminder.Items.IndexOf(t);
            if (idx < 0)
            {
                MessageBox.Show(this, "目前的文字不在選單中，無法刪除。\n（請先從下拉選單選一個項目）",
                    "刪除選項", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            cboReminder.Items.RemoveAt(idx);
            SaveReminders();
            cboReminder.Text = cboReminder.Items.Count > 0 ? cboReminder.Items[0].ToString() : "";
        }

        // ---- 編輯清單項目 ----
        private void EnterEdit(Alarm a)
        {
            editing = a;
            cboReminder.Text = a.Text;
            soundPath = a.SoundFile;
            txtSound.Text = SoundLabel();

            // 停留時間
            if (a.StaySeconds <= 0) chkNoAuto.Checked = true;
            else
            {
                chkNoAuto.Checked = false;
                if (a.StaySeconds % 60 == 0) { cboStayUnit.SelectedIndex = 0; numStay.Value = Math.Min(a.StaySeconds / 60, 999); }
                else { cboStayUnit.SelectedIndex = 1; numStay.Value = Math.Min(a.StaySeconds, 999); }
            }

            if (a.Countdown)
            {
                numCdMin.Value = Math.Min(a.IntervalSeconds / 60, 1440);
                numCdSec.Value = a.IntervalSeconds % 60;
                chkLoop.Checked = a.Loop;
            }
            else
            {
                timePicker.Value = DateTime.Today.AddHours(a.Hour).AddMinutes(a.Minute).AddSeconds(a.Second);
                chkRepeat.Checked = a.Repeat;          // 觸發 UpdateDayBoxes
                for (int i = 0; i < 7; i++) chkDays[i].Checked = a.Repeat && (a.Days & (1 << i)) != 0;
            }

            // 「✓ 更新」放在被編輯項目對應的群組按鈕，另一個群組按鈕當「取消編輯」
            if (a.Countdown)
            {
                btnCountdown.Text = "✓ 更新"; btnAdd.Text = "✗ 取消編輯";
                grpCountdown.Text = "編輯倒數鬧鐘"; grpClock.Text = "指定時間鬧鐘";
            }
            else
            {
                btnAdd.Text = "✓ 更新"; btnCountdown.Text = "✗ 取消編輯";
                grpClock.Text = "編輯鬧鐘"; grpCountdown.Text = "倒數計時";
            }
        }

        private void ExitEdit()
        {
            editing = null;
            btnAdd.Text = "＋ 新增鬧鐘";
            btnCountdown.Text = "＋ 倒數新增";
            grpClock.Text = "指定時間鬧鐘";
            grpCountdown.Text = "倒數計時";
        }

        private void UpdateEditing()
        {
            var a = editing;
            if (a == null) return;
            a.Text = CurrentText();
            a.SoundFile = soundPath;
            a.StaySeconds = CurrentStaySeconds();

            if (a.Countdown)
            {
                int iv = (int)numCdMin.Value * 60 + (int)numCdSec.Value;
                if (iv <= 0) iv = 1;
                a.IntervalSeconds = iv;
                a.Loop = chkLoop.Checked;
                a.Target = DateTime.Now.AddSeconds(iv);
                a.Enabled = true;
            }
            else
            {
                int h = timePicker.Value.Hour, m = timePicker.Value.Minute, sec = timePicker.Value.Second;
                foreach (var o in alarms)
                    if (o != a && !o.Countdown && o.Hour == h && o.Minute == m && o.Second == sec)
                    {
                        MessageBox.Show(this, string.Format("已存在 {0:00}:{1:00}:{2:00} 的鬧鐘。", h, m, sec),
                            "重複時間", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                a.Hour = h; a.Minute = m; a.Second = sec;
                a.Repeat = chkRepeat.Checked;
                int days = 0;
                if (chkRepeat.Checked) for (int i = 0; i < 7; i++) if (chkDays[i].Checked) days |= (1 << i);
                a.Days = days;
                a.LastFired = null;
            }
            SaveAlarms(); RefreshList(); ExitEdit();
        }

        // 清單項目自繪：停用或暫停中的項目以灰色顯示
        private void LstAlarms_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= lstAlarms.Items.Count) return;
            var a = (Alarm)lstAlarms.Items[e.Index];
            bool inactive = paused || !a.Enabled;
            bool selected = (e.State & DrawItemState.Selected) != 0;

            Color bg = selected ? Color.FromArgb(224, 228, 247)
                                 : (e.Index % 2 == 0 ? Color.White : Color.FromArgb(247, 248, 252));
            using (var bb = new SolidBrush(bg)) e.Graphics.FillRectangle(bb, e.Bounds);

            Color fg = inactive ? Color.FromArgb(165, 168, 175) : Color.FromArgb(30, 32, 42);
            TextRenderer.DrawText(e.Graphics, a.ToString(), e.Font,
                Rectangle.Inflate(e.Bounds, -4, 0), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        // ---- 暫停（停止所有鬧鐘）----
        private void OnMasterStopChanged()
        {
            if (loadingSettings) return;
            paused = chkMasterStop.Checked;
            if (!paused) RebaseSchedule();   // 取消暫停 → 重新排程列表時間
            SaveSettings();
            tray.Text = paused ? "桌面鬧鐘（已暫停）" : "桌面鬧鐘（執行中）";
            lstAlarms.Invalidate();          // 重畫以反灰/恢復清單
        }

        private void RebaseSchedule()
        {
            DateTime now = DateTime.Now;
            foreach (var a in alarms)
            {
                if (!a.Enabled) continue;
                if (a.Countdown)
                {
                    if (a.IntervalSeconds > 0) a.Target = now.AddSeconds(a.IntervalSeconds);
                }
                else
                {
                    a.LastFired = null;   // 允許今天稍後再次觸發
                }
            }
            RefreshList();
            SaveAlarms();
        }

        private int CurrentStaySeconds()
        {
            if (chkNoAuto.Checked) return 0;
            int v = (int)numStay.Value;
            return cboStayUnit.SelectedIndex == 0 ? v * 60 : v;  // 分 : 秒
        }

        private string SoundLabel()
        {
            if (string.IsNullOrEmpty(soundPath)) return "（預設系統音效）";
            if (string.Equals(soundPath, DefaultSoundFile, StringComparison.OrdinalIgnoreCase))
                return "（預設鈴聲）" + Path.GetFileName(soundPath);
            return soundPath;
        }

        private string UiImageLabel()
        {
            if (string.IsNullOrEmpty(uiImagePath)) return "（預設時鐘造型）";
            return Path.GetFileName(uiImagePath);
        }

        private void BrowseUiImage()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "選擇動態UI圖片（建議去背 PNG）";
                dlg.Filter = "圖片檔 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有檔案 (*.*)|*.*";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    uiImagePath = dlg.FileName;
                    txtUiImage.Text = UiImageLabel();
                    SaveSettings();
                }
            }
        }

        private void BrowseSound()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "選擇響鈴音樂檔";
                dlg.Filter = "音樂檔 (*.wav;*.mp3;*.wma)|*.wav;*.mp3;*.wma|所有檔案 (*.*)|*.*";
                dlg.InitialDirectory = @"C:\Windows\Media";
                if (dlg.ShowDialog() == DialogResult.OK)
                {
                    soundPath = dlg.FileName;
                    txtSound.Text = SoundLabel();
                }
            }
        }

        private void TestSound()
        {
            StopTest();
            if (string.IsNullOrEmpty(soundPath)) { SystemSounds.Exclamation.Play(); return; }
            if (!File.Exists(soundPath)) { MessageBox.Show("找不到音樂檔：" + soundPath); return; }
            testPlayer = new MciPlayer();
            if (testPlayer.Open(soundPath)) testPlayer.Play();
            else MessageBox.Show("無法播放此音樂檔，請改用 wav/mp3/wma 格式。");
        }

        private void StopTest()
        {
            if (testPlayer != null) { testPlayer.Close(); testPlayer = null; }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                tray.ShowBalloonTip(2000, "桌面鬧鐘", "已縮小到系統匣，仍會在背景提醒。", ToolTipIcon.Info);
            }
        }

        private void ShowFromTray()
        {
            Show(); WindowState = FormWindowState.Normal; Activate();
        }

        private string CurrentText()
        {
            return string.IsNullOrWhiteSpace(cboReminder.Text) ? "時間到了！" : cboReminder.Text.Trim();
        }

        private void AddClockAlarm()
        {
            int h = timePicker.Value.Hour, m = timePicker.Value.Minute, sec = timePicker.Value.Second;

            // 同一時間不重複新增
            foreach (var a in alarms)
            {
                if (!a.Countdown && a.Hour == h && a.Minute == m && a.Second == sec)
                {
                    MessageBox.Show(this,
                        string.Format("已存在 {0:00}:{1:00}:{2:00} 的鬧鐘，請勿重複新增。", h, m, sec),
                        "重複時間", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }

            int days = 0;
            if (chkRepeat.Checked)
                for (int i = 0; i < 7; i++) if (chkDays[i].Checked) days |= (1 << i);

            alarms.Add(new Alarm
            {
                Countdown = false,
                Hour = h, Minute = m, Second = sec,
                Text = CurrentText(),
                Repeat = chkRepeat.Checked,
                Days = days,
                SoundFile = soundPath,
                StaySeconds = CurrentStaySeconds()
            });
            SaveAlarms(); RefreshList();
        }

        private void AddCountdownAlarm()
        {
            int intervalSec = (int)numCdMin.Value * 60 + (int)numCdSec.Value;
            if (intervalSec <= 0) intervalSec = 1;   // 至少 1 秒
            DateTime target = DateTime.Now.AddSeconds(intervalSec);
            alarms.Add(new Alarm
            {
                Countdown = true,
                Target = target,
                Text = CurrentText(),
                SoundFile = soundPath,
                Loop = chkLoop.Checked,
                IntervalSeconds = intervalSec,
                StaySeconds = CurrentStaySeconds()
            });
            SaveAlarms(); RefreshList();
            string loopMsg = chkLoop.Checked ? string.Format("，之後每 {0} 循環提醒", Alarm.DurText(intervalSec)) : "";
            tray.ShowBalloonTip(2500, "桌面鬧鐘",
                string.Format("已設定 {0} 後（{1:HH:mm:ss}）提醒{2}。", Alarm.DurText(intervalSec), target, loopMsg),
                ToolTipIcon.Info);
        }

        private void DeleteSelected()
        {
            var a = lstAlarms.SelectedItem as Alarm;
            if (a == null) return;
            if (a == editing) ExitEdit();
            alarms.Remove(a); SaveAlarms(); RefreshList();
        }

        private void ToggleSelected()
        {
            var a = lstAlarms.SelectedItem as Alarm;
            if (a == null) return;
            a.Enabled = !a.Enabled; SaveAlarms(); RefreshList();
        }

        private void RefreshList()
        {
            var a = lstAlarms.SelectedItem as Alarm;   // 以物件保留選取
            var view = new List<Alarm>(alarms);
            int s = cboSort != null ? cboSort.SelectedIndex : 0;
            if (s == 1) view.Sort((x, y) => x.SortKey().CompareTo(y.SortKey()));
            else if (s == 2) view.Sort((x, y) => y.SortKey().CompareTo(x.SortKey()));

            lstAlarms.BeginUpdate();
            lstAlarms.Items.Clear();
            foreach (var item in view) lstAlarms.Items.Add(item);
            if (a != null) lstAlarms.SelectedItem = a;
            lstAlarms.EndUpdate();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            lblClock.Text = now.ToString("yyyy/MM/dd  HH:mm:ss") + (paused ? "   ⏸ 已暫停" : "");
            if (paused) return;   // 暫停時不執行任何列表項目

            foreach (var a in alarms)
            {
                if (!a.Enabled) continue;

                if (a.Countdown)
                {
                    if (now >= a.Target)
                    {
                        if (a.Loop && a.IntervalSeconds > 0)
                            a.Target = now.AddSeconds(a.IntervalSeconds);
                        else
                            a.Enabled = false;
                        RefreshList(); SaveAlarms();
                        Trigger(a);
                    }
                    continue;
                }

                if (a.Hour == now.Hour && a.Minute == now.Minute && a.Second == now.Second)
                {
                    // 指定星期才響（Repeat 且 Days!=0 時）
                    if (a.Repeat && a.Days != 0 && (a.Days & (1 << (int)now.DayOfWeek)) == 0)
                        continue;
                    if (a.LastFired.HasValue && (now - a.LastFired.Value).TotalSeconds < 1.5)
                        continue;   // 同一秒不重複觸發
                    a.LastFired = now;
                    if (!a.Repeat) { a.Enabled = false; RefreshList(); SaveAlarms(); }
                    Trigger(a);
                }
            }
        }

        private void Trigger(Alarm a)
        {
            string bigTime = a.Countdown
                ? DateTime.Now.ToString("HH:mm:ss")
                : string.Format("{0:00}:{1:00}:{2:00}", a.Hour, a.Minute, a.Second);
            new AlarmPopup(a.Text, bigTime, a.SoundFile, a.StaySeconds, uiImagePath).Show();

            // 同時推播到 LINE 群組
            if (lineOn && lineToken.Length > 0 && lineTo.Length > 0)
            {
                string msg = "⏰ " + bigTime + "  " + a.Text;
                string tok = lineToken, to = lineTo;
                var th = new System.Threading.Thread(() => SendLine(tok, to, msg)) { IsBackground = true };
                th.Start();
            }
        }

        // 呼叫 LINE Messaging API push 發送文字訊息；回傳結果字串（成功/失敗原因）
        private static string SendLine(string token, string to, string text)
        {
            try
            {
                System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072; // TLS 1.2
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://api.line.me/v2/bot/message/push");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.Headers["Authorization"] = "Bearer " + token;
                string body = "{\"to\":\"" + to + "\",\"messages\":[{\"type\":\"text\",\"text\":\"" + JsonEsc(text) + "\"}]}";
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                req.ContentLength = bytes.Length;
                using (var st = req.GetRequestStream()) st.Write(bytes, 0, bytes.Length);
                using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    return "成功（HTTP " + (int)resp.StatusCode + "）";
            }
            catch (System.Net.WebException ex)
            {
                string detail = ex.Message;
                try { using (var r = new StreamReader(ex.Response.GetResponseStream())) detail = r.ReadToEnd(); }
                catch { }
                return "失敗：" + detail;
            }
            catch (Exception ex) { return "失敗：" + ex.Message; }
        }

        private static string JsonEsc(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }

        private void LoadAlarms()
        {
            try
            {
                if (!File.Exists(savePath)) return;
                foreach (var line in File.ReadAllLines(savePath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    var a = Alarm.Deserialize(line);
                    if (a != null) alarms.Add(a);
                }
            }
            catch { }
        }

        private void SaveAlarms()
        {
            try
            {
                var lines = new List<string>();
                foreach (var a in alarms) lines.Add(a.Serialize());
                File.WriteAllLines(savePath, lines.ToArray());
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                loadingSettings = true;
                if (File.Exists(settingsPath))
                {
                    foreach (var line in File.ReadAllLines(settingsPath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                        if (key == "paused") paused = val.Trim() == "1";
                        else if (key == "line_on") lineOn = val.Trim() == "1";
                        else if (key == "line_token") lineToken = val;
                        else if (key == "line_to") lineTo = val;
                        else if (key == "ui_image") uiImagePath = val;
                    }
                }
                chkMasterStop.Checked = paused;
                tray.Text = paused ? "桌面鬧鐘（已暫停）" : "桌面鬧鐘（執行中）";
                if (txtUiImage != null) txtUiImage.Text = UiImageLabel();
            }
            catch { }
            finally { loadingSettings = false; }
        }

        private void SaveSettings()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("paused=" + (paused ? "1" : "0"));
                sb.AppendLine("line_on=" + (lineOn ? "1" : "0"));
                sb.AppendLine("line_token=" + lineToken);
                sb.AppendLine("line_to=" + lineTo);
                sb.AppendLine("ui_image=" + uiImagePath);
                File.WriteAllText(settingsPath, sb.ToString());
            }
            catch { }
        }

        // LINE 通知設定對話框
        private void ShowLineDialog()
        {
            using (var dlg = new Form())
            {
                dlg.Text = "LINE 群組通知設定";
                dlg.ClientSize = new Size(460, 250);
                dlg.FormBorderStyle = FormBorderStyle.FixedDialog;
                dlg.StartPosition = FormStartPosition.CenterParent;
                dlg.MaximizeBox = false; dlg.MinimizeBox = false;
                dlg.Font = new Font("Microsoft JhengHei", 9F);
                try { dlg.Icon = Icon; } catch { }

                var chk = new CheckBox { Text = "啟用 LINE 群組通知（鬧鐘響時同步發送）", Left = 16, Top = 14, Width = 420, Checked = lineOn };
                var lblTok = new Label { Text = "Channel access token：", Left = 16, Top = 48, Width = 430 };
                var txtTok = new TextBox { Left = 16, Top = 70, Width = 428, Text = lineToken };
                var lblTo = new Label { Text = "群組 ID（groupId，C 開頭）：", Left = 16, Top = 104, Width = 430 };
                var txtTo = new TextBox { Left = 16, Top = 126, Width = 428, Text = lineTo };
                var lblResult = new Label { Left = 16, Top = 162, Width = 428, Height = 36, ForeColor = Color.DimGray };

                var btnTestLine = new Button { Text = "測試發送", Left = 16, Top = 206, Width = 110, Height = 30 };
                btnTestLine.Click += (s, e) =>
                {
                    string tk = txtTok.Text.Trim(), to = txtTo.Text.Trim();
                    if (tk.Length == 0 || to.Length == 0) { lblResult.Text = "請先填入 token 與群組 ID。"; return; }
                    lblResult.Text = "發送中…";
                    Application.DoEvents();
                    lblResult.Text = "測試結果：" + SendLine(tk, to, "✅ 桌面鬧鐘 LINE 通知測試");
                };
                var btnOk = new Button { Text = "儲存", Left = 244, Top = 206, Width = 95, Height = 30, DialogResult = DialogResult.OK };
                var btnCancel = new Button { Text = "取消", Left = 349, Top = 206, Width = 95, Height = 30, DialogResult = DialogResult.Cancel };

                dlg.Controls.AddRange(new Control[] { chk, lblTok, txtTok, lblTo, txtTo, lblResult, btnTestLine, btnOk, btnCancel });
                dlg.AcceptButton = btnOk; dlg.CancelButton = btnCancel;

                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    lineOn = chk.Checked;
                    lineToken = txtTok.Text.Trim();
                    lineTo = txtTo.Text.Trim();
                    SaveSettings();
                }
            }
        }
    }

    // 響鈴彈窗：鬧鐘造型(含兩隻腳)在螢幕最下方左右走動，停留時間到後定點停住，再自動關閉
    public class AlarmPopup : Form
    {
        private const int FrameMs = 30;
        private const int ParkMs = 3000;
        private const int DefaultMoveMs = 10000;

        private Timer animTimer;
        private int elapsedMs = 0;
        private int lastSec = -1;
        private int dx = 1;            // 水平速度（像素/幀，放慢以避免提示框殘影）
        private bool moving = true;
        private double legPhase = 0;   // 走路擺腳相位

        private readonly string message;
        private readonly string timeText;
        private readonly string soundFile;
        private readonly int staySeconds;
        private readonly int moveDurationMs;
        private readonly int closeAtMs;
        private readonly int soundStopSec;

        private MciPlayer player;
        private SoundPlayer wavLoop;
        private bool useSystemBeep;
        private bool soundStopped;
        private readonly string imagePath;
        private Image charImage;      // 自訂角色圖（null=用預設時鐘造型）

        // 版面
        private Rectangle bubbleRect = new Rectangle(12, 8, 416, 76);
        private Rectangle btnSnoozeRect = new Rectangle(112, 54, 100, 24);
        private Rectangle btnOkRect = new Rectangle(228, 54, 100, 24);
        private const int ClockCx = 220;
        private const int ClockCy = 188;
        private const float ClockR = 70f;
        private Bitmap canvas;
        private int winX, winY;

        public AlarmPopup(string message, string timeText, string soundFile, int staySeconds, string imagePath)
        {
            this.message = message;
            this.timeText = timeText;
            this.soundFile = soundFile;
            this.staySeconds = staySeconds;
            this.imagePath = imagePath;
            if (!string.IsNullOrEmpty(imagePath) && File.Exists(imagePath))
            {
                try { using (var tmp = Image.FromFile(imagePath)) charImage = new Bitmap(tmp); }
                catch { charImage = null; }
            }
            if (charImage != null)
            {
                // 自訂圖片無「肚子」放字：泡泡加高以容納 時間 + 提醒文字 + 按鈕
                bubbleRect = new Rectangle(12, 8, 416, 100);
                btnSnoozeRect = new Rectangle(112, 74, 100, 24);
                btnOkRect = new Rectangle(228, 74, 100, 24);
            }

            bool autoClose = staySeconds > 0;
            moveDurationMs = autoClose ? staySeconds * 1000 : DefaultMoveMs;
            closeAtMs = autoClose ? moveDurationMs + ParkMs : -1;
            soundStopSec = Math.Min(autoClose ? staySeconds : DefaultMoveMs / 1000, 60);

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Width = 440; Height = 280;
            TopMost = true;
            ShowInTaskbar = false;
            Font = new Font("Microsoft JhengHei", 9F);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            // 分層視窗無子控制項，改用座標判斷點擊（按鈕僅在停住顯示後才生效）
            MouseDown += (s, e) =>
            {
                if (btnSnoozeRect.Contains(e.Location)) { Snooze(); Close(); }
                else if (btnOkRect.Contains(e.Location)) { Close(); }
                else if (e.Y > bubbleRect.Bottom) { Close(); }   // 點角色本體也可關閉
            };

            Shown += (s, e) =>
            {
                canvas = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                PositionAtBottom();
                StartSound();
                StartAnimation();
                Render();
            };
            FormClosed += (s, e) => StopAll();
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x80000; return cp; }  // WS_EX_LAYERED
        }

        private void PositionAtBottom()
        {
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            winX = wa.Left + 30;
            winY = wa.Bottom - Height;   // 緊貼螢幕最下方
            Location = new Point(winX, winY);
        }

        private void Render()
        {
            if (canvas == null) return;
            using (var g = Graphics.FromImage(canvas))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                DrawBubble(g);                       // 對話框永久顯示（走動時也在）
                if (charImage != null) DrawImageChar(g);
                else DrawClock(g);
                DrawButton(g, btnSnoozeRect, "稍後 5 分");
                DrawButton(g, btnOkRect, "我知道了");
            }
            SetBitmap(canvas, winX, winY);
        }

        private void DrawButton(Graphics g, Rectangle r, string text)
        {
            using (var path = RoundRect(r, 6))
            using (var fill = new SolidBrush(Color.White))                       // 白色底
            using (var border = new Pen(Color.FromArgb(150, 150, 150), 1.4f))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            using (var f = new Font("Microsoft JhengHei", 9.5F))
            using (var b = new SolidBrush(Color.FromArgb(40, 40, 40)))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                g.DrawString(text, f, b, r, sf);
        }

        // 逐像素 Alpha 貼到分層視窗：邊緣平滑、無背景去背色光暈
        private void SetBitmap(Bitmap bmp, int x, int y)
        {
            if (!IsHandleCreated) return;
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBmp = SelectObject(memDc, hBitmap);
            try
            {
                SIZE size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                POINT src = new POINT { x = 0, y = 0 };
                POINT dst = new POINT { x = x, y = y };
                BLENDFUNCTION blend = new BLENDFUNCTION
                {
                    BlendOp = 0, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = 1  // AC_SRC_ALPHA
                };
                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, 2);  // ULW_ALPHA
            }
            finally
            {
                SelectObject(memDc, oldBmp);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        #region Win32 分層視窗
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential)] private struct BLENDFUNCTION
        { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr hObject);
        [DllImport("user32.dll")] private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pprSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        #endregion

        private void DrawBubble(Graphics g)
        {
            using (var path = RoundRect(bubbleRect, 16))
            using (var fill = new SolidBrush(Color.White))
            using (var border = new Pen(Color.Black, 4))   // 黑色粗框強調
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
                // 白底三角覆蓋泡泡底框的缺口，使尖角與泡泡連成一體
                g.FillPolygon(fill, new Point[]
                {
                    new Point(ClockCx - 16, bubbleRect.Bottom - 5),
                    new Point(ClockCx + 16, bubbleRect.Bottom - 5),
                    new Point(ClockCx, bubbleRect.Bottom + 16)
                });
                // 黑粗線只描尖角兩條外緣
                g.DrawLines(border, new Point[]
                {
                    new Point(ClockCx - 14, bubbleRect.Bottom - 1),
                    new Point(ClockCx, bubbleRect.Bottom + 16),
                    new Point(ClockCx + 14, bubbleRect.Bottom - 1)
                });
            }

            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var bTime = new SolidBrush(Color.Firebrick))
            using (var bMsg = new SolidBrush(Color.FromArgb(40, 40, 40)))
            {
                if (charImage != null)
                {
                    // 自訂圖片無「肚子」可放字：時間 + 提醒文字都放泡泡
                    using (var fTime = new Font("Consolas", 19F, FontStyle.Bold))
                    using (var fMsg = new Font("Microsoft JhengHei", 12F, FontStyle.Bold))
                    {
                        g.DrawString(timeText, fTime, bTime,
                            new RectangleF(bubbleRect.Left, bubbleRect.Top + 4, bubbleRect.Width, 30), sf);
                        string disp = message ?? "";
                        if (disp.Length > 16) disp = disp.Substring(0, 16) + "…";
                        g.DrawString(disp, fMsg, bMsg,
                            new RectangleF(bubbleRect.Left + 6, bubbleRect.Top + 36, bubbleRect.Width - 12, 28), sf);
                    }
                }
                else
                {
                    // 預設時鐘：泡泡只顯示時間（提醒文字在時鐘肚子）
                    using (var fTime = new Font("Consolas", 24F, FontStyle.Bold))
                        g.DrawString(timeText, fTime, bTime,
                            new RectangleF(bubbleRect.Left, bubbleRect.Top + 2, bubbleRect.Width, 42), sf);
                }
            }
        }

        // 畫自訂角色圖：等比例縮放置中，走動時上下擺動
        private void DrawImageChar(Graphics g)
        {
            const float maxW = 184f, maxH = 162f;
            float iw = charImage.Width, ih = charImage.Height;
            float scale = Math.Min(maxW / iw, maxH / ih);
            float dw = iw * scale, dh = ih * scale;
            float bob = moving ? -(float)Math.Abs(Math.Sin(legPhase)) * 6f : 0f;
            float dxp = ClockCx - dw / 2f;
            float dyp = 208f - dh / 2f + bob;   // 置於泡泡下方區域中央
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(charImage, dxp, dyp, dw, dh);
        }

        // 時鐘造型：紅色鐘體 + 金色鈴鐺，肚子（白色錶面）放提醒文字（無眼睛、無指針）
        private void DrawClock(Graphics g)
        {
            float cx = ClockCx, cy = ClockCy, r = ClockR;
            var gold = new SolidBrush(Color.FromArgb(255, 196, 0));
            var red = new SolidBrush(Color.FromArgb(229, 57, 53));
            var redDark = new SolidBrush(Color.FromArgb(183, 28, 28));
            var white = Brushes.White;
            var penBody = new Pen(Color.FromArgb(183, 28, 28), 4);
            var penFace = new Pen(Color.FromArgb(224, 224, 224), 2);
            var legPen = new Pen(Color.FromArgb(183, 28, 28), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round };

            // 兩隻腳（走路擺動）
            float swing = (float)(Math.Sin(legPhase) * 6);
            float ly = cy + r * 0.80f, lendY = cy + r * 1.02f;
            float lxL = cx - r * 0.42f, lxR = cx + r * 0.42f;
            g.DrawLine(legPen, lxL, ly, lxL - swing, lendY);
            g.DrawLine(legPen, lxR, ly, lxR + swing, lendY);
            g.FillEllipse(redDark, lxL - swing - 13, lendY - 5, 28, 20);
            g.FillEllipse(redDark, lxR + swing - 15, lendY - 5, 28, 20);

            // 兩側鈴鐺（縮小）+ 頂部按鈕
            float rb = r * 0.38f;
            g.FillEllipse(gold, cx - r * 0.52f - rb, cy - r * 0.92f - rb, rb * 2, rb * 2);
            g.FillEllipse(gold, cx + r * 0.52f - rb, cy - r * 0.92f - rb, rb * 2, rb * 2);
            g.FillEllipse(gold, cx - 5, cy - r - 10, 10, 9);

            // 鐘體
            g.FillEllipse(red, cx - r, cy - r, r * 2, r * 2);
            g.DrawEllipse(penBody, cx - r, cy - r, r * 2, r * 2);
            // 白色錶面（肚子）
            float rf = r * 0.80f;
            g.FillEllipse(white, cx - rf, cy - rf, rf * 2, rf * 2);
            g.DrawEllipse(penFace, cx - rf, cy - rf, rf * 2, rf * 2);

            // 提醒文字放肚子中間：最多 10 字，超過以 … 替代；字級自動縮放
            string disp = message ?? "";
            if (disp.Length > 10) disp = disp.Substring(0, 10) + "…";
            float box = rf * 1.40f;
            var tr = new RectangleF(cx - box / 2, cy - box / 2, box, box);
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var tb = new SolidBrush(Color.FromArgb(40, 40, 40)))
            using (var f = FitFont(g, disp, box, box))
                g.DrawString(disp, f, tb, tr, sf);

            gold.Dispose(); red.Dispose(); redDark.Dispose();
            penBody.Dispose(); penFace.Dispose(); legPen.Dispose();
        }

        // 由大到小挑出能塞進指定方框的字級
        private static Font FitFont(Graphics g, string text, float maxW, float maxH)
        {
            if (string.IsNullOrEmpty(text)) text = " ";
            for (float size = 15f; size > 6f; size -= 1f)
            {
                var f = new Font("Microsoft JhengHei", size, FontStyle.Bold);
                SizeF sz = g.MeasureString(text, f, (int)maxW);
                if (sz.Width <= maxW && sz.Height <= maxH) return f;
                f.Dispose();
            }
            return new Font("Microsoft JhengHei", 6.5f, FontStyle.Bold);
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            int d = radius * 2;
            var p = new GraphicsPath();
            p.AddArc(r.Left, r.Top, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        private void StartSound()
        {
            bool started = false;
            // 未指定或找不到檔案時，回退到內建預設鈴聲
            string file = soundFile;
            if (string.IsNullOrEmpty(file) || !File.Exists(file)) file = MainForm.DefaultSoundFile;

            if (!string.IsNullOrEmpty(file) && File.Exists(file))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext == ".wav")
                {
                    try { wavLoop = new SoundPlayer(file); wavLoop.PlayLooping(); started = true; }
                    catch { }
                }
                if (!started)
                {
                    player = new MciPlayer();
                    if (player.Open(file)) { player.Play(); started = true; }
                }
            }
            if (!started) { useSystemBeep = true; SystemSounds.Exclamation.Play(); }
        }

        private void StartAnimation()
        {
            animTimer = new Timer { Interval = FrameMs };
            animTimer.Tick += (s, e) =>
            {
                elapsedMs += FrameMs;

                if (moving)
                {
                    Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                    int nx = winX + dx;
                    if (nx <= wa.Left) { nx = wa.Left; dx = Math.Abs(dx); }
                    else if (nx + Width >= wa.Right) { nx = wa.Right - Width; dx = -Math.Abs(dx); }
                    winX = nx;
                    legPhase += 0.10;
                    if (elapsedMs >= moveDurationMs) { moving = false; legPhase = 0; } // 定點停住
                    Render();   // 更新位置與擺腳
                }

                int sec = elapsedMs / 1000;
                if (sec != lastSec)
                {
                    lastSec = sec;
                    if (!soundStopped)
                    {
                        if (useSystemBeep && sec % 2 == 0) SystemSounds.Exclamation.Play();
                        if (player != null && !player.IsPlaying()) player.Play();
                        if (sec >= soundStopSec) { StopSoundOnly(); soundStopped = true; }
                    }
                }

                if (closeAtMs > 0 && elapsedMs >= closeAtMs) Close();
            };
            animTimer.Start();
        }

        private void StopSoundOnly()
        {
            if (player != null) { player.Close(); player = null; }
            if (wavLoop != null) { try { wavLoop.Stop(); } catch { } wavLoop = null; }
            useSystemBeep = false;
        }

        private void StopAll()
        {
            if (animTimer != null) { animTimer.Stop(); animTimer.Dispose(); animTimer = null; }
            StopSoundOnly();
            if (canvas != null) { canvas.Dispose(); canvas = null; }
            if (charImage != null) { charImage.Dispose(); charImage = null; }
        }

        private void Snooze()
        {
            string m = message, tt = timeText, sf = soundFile, ip = imagePath;
            int ss = staySeconds;
            var t = new Timer { Interval = 5 * 60 * 1000 };
            t.Tick += (s, e) =>
            {
                t.Stop(); t.Dispose();
                new AlarmPopup(m, tt, sf, ss, ip).Show();
            };
            t.Start();
        }
    }

    static class Program
    {
        public const string MutexName = @"Local\DEOM_AlarmClock_SingleInstance";
        public const string ShowEventName = @"Local\DEOM_AlarmClock_Show";

        [STAThread]
        static void Main()
        {
            bool createdNew;
            var mutex = new System.Threading.Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                // 已有一個在執行：通知它把視窗叫回前景，然後自己結束（避免多開造成設定混亂）
                try
                {
                    var ev = System.Threading.EventWaitHandle.OpenExisting(ShowEventName);
                    ev.Set();
                }
                catch { }
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            GC.KeepAlive(mutex);   // 讓 mutex 存活至程式結束
        }
    }
}
