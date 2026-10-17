using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using System.Diagnostics;

namespace OkiroApp
{
    public class AlarmItem
    {
        public string Id { get; set; }
        public string Time { get; set; }
        public string Label { get; set; }
        public string Days { get; set; }
        public string AudioPath { get; set; }
        public bool Enabled { get; set; }
    }

    public class MainForm : Form
    {
        [DllImport("winmm.dll")]
        private static extern long mciSendString(string strCommand, StringBuilder strReturn, int iReturnLength, IntPtr hwndCallback);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private Panel sidebar;
        private Panel mainArea;
        private Panel alarmsView;
        private Panel stopwatchView;
        private FlowLayoutPanel alarmsListPanel;
        private Label liveTimeLabel;
        private Label liveDateLabel;
        private Button navAlarmsBtn;
        private Button navStopwatchBtn;

        private List<AlarmItem> alarms = new List<AlarmItem>();
        private HashSet<string> triggeredThisMinute = new HashSet<string>();
        private bool isRinging = false;
        private string dataFile;

        // Stopwatch
        private Stopwatch stopwatch = new Stopwatch();
        private Label swDisplay;
        private Button swStartBtn;

        // Theme Colors (Windows 11 Dark)
        private Color bgDark = Color.FromArgb(32, 32, 32);
        private Color bgSidebar = Color.FromArgb(25, 25, 25);
        private Color bgCard = Color.FromArgb(43, 43, 43);
        private Color accentBlue = Color.FromArgb(0, 120, 212);
        private Color textWhite = Color.FromArgb(245, 245, 245);
        private Color textMuted = Color.FromArgb(160, 160, 160);

        public MainForm()
        {
            this.Text = "Okiro";
            this.Size = new Size(800, 560);
            this.MinimumSize = new Size(680, 480);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = bgDark;
            this.ForeColor = textWhite;
            this.Font = new Font("Segoe UI", 10F, FontStyle.Regular);
            this.DoubleBuffered = true;

            // Enable Windows 11 Dark Title Bar
            try {
                int useDarkMode = 1;
                DwmSetWindowAttribute(this.Handle, 20, ref useDarkMode, sizeof(int));
            } catch { }

            dataFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alarms_data.txt");
            LoadAlarms();
            BuildUI();

            Timer clockTimer = new Timer { Interval = 200 };
            clockTimer.Tick += ClockTimer_Tick;
            clockTimer.Start();
        }

        private void BuildUI()
        {
            // Sidebar
            sidebar = new Panel { Dock = DockStyle.Left, Width = 200, BackColor = bgSidebar };
            Label appTitle = new Label {
                Text = "Okiro",
                Font = new Font("Segoe UI Semibold", 18F, FontStyle.Bold),
                ForeColor = textWhite,
                Location = new Point(22, 22),
                AutoSize = true
            };
            sidebar.Controls.Add(appTitle);

            navAlarmsBtn = CreateNavButton("⏰   Alarms", 80, true);
            navAlarmsBtn.Click += (s, e) => SwitchView("alarms");
            sidebar.Controls.Add(navAlarmsBtn);

            navStopwatchBtn = CreateNavButton("⏱   Stopwatch", 130, false);
            navStopwatchBtn.Click += (s, e) => SwitchView("stopwatch");
            sidebar.Controls.Add(navStopwatchBtn);

            Panel clockCard = new Panel {
                Size = new Size(172, 82),
                Location = new Point(14, this.ClientSize.Height - 98),
                BackColor = bgCard,
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            liveTimeLabel = new Label {
                Text = DateTime.Now.ToString("HH:mm:ss"),
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = textWhite,
                Dock = DockStyle.Top,
                Height = 45,
                TextAlign = ContentAlignment.BottomCenter
            };
            liveDateLabel = new Label {
                Text = DateTime.Now.ToString("dddd, MMM dd"),
                Font = new Font("Segoe UI", 9.5F),
                ForeColor = textMuted,
                Dock = DockStyle.Bottom,
                Height = 32,
                TextAlign = ContentAlignment.TopCenter
            };
            clockCard.Controls.Add(liveTimeLabel);
            clockCard.Controls.Add(liveDateLabel);
            sidebar.Controls.Add(clockCard);

            // Main Area
            mainArea = new Panel { Dock = DockStyle.Fill, BackColor = bgDark, Padding = new Padding(28, 22, 28, 22) };

            BuildAlarmsView();
            BuildStopwatchView();

            this.Controls.Add(mainArea);
            this.Controls.Add(sidebar);
            SwitchView("alarms");
        }

        private Button CreateNavButton(string text, int y, bool active)
        {
            Button btn = new Button {
                Text = "  " + text,
                Location = new Point(12, y),
                Size = new Size(176, 42),
                FlatStyle = FlatStyle.Flat,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 11F),
                BackColor = active ? accentBlue : bgSidebar,
                ForeColor = textWhite,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }

        private void BuildAlarmsView()
        {
            alarmsView = new Panel { Dock = DockStyle.Fill };

            Panel header = new Panel { Dock = DockStyle.Top, Height = 55 };
            Label title = new Label {
                Text = "Alarms",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 4)
            };

            Button addBtn = new Button {
                Text = "+ Add an alarm",
                Size = new Size(140, 38),
                Location = new Point(header.Width - 145, 6),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                FlatStyle = FlatStyle.Flat,
                BackColor = accentBlue,
                ForeColor = textWhite,
                Font = new Font("Segoe UI Semibold", 10.5F),
                Cursor = Cursors.Hand
            };
            addBtn.FlatAppearance.BorderSize = 0;
            addBtn.Click += (s, e) => OpenAlarmEditor(null);

            header.Controls.Add(title);
            header.Controls.Add(addBtn);

            alarmsListPanel = new FlowLayoutPanel {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Padding = new Padding(0, 10, 0, 10)
            };
            alarmsListPanel.SizeChanged += (s, e) => {
                foreach (Control c in alarmsListPanel.Controls) c.Width = alarmsListPanel.ClientSize.Width - 22;
            };

            alarmsView.Controls.Add(alarmsListPanel);
            alarmsView.Controls.Add(header);
            mainArea.Controls.Add(alarmsView);

            RenderAlarms();
        }

        private void BuildStopwatchView()
        {
            stopwatchView = new Panel { Dock = DockStyle.Fill, Visible = false };
            Label title = new Label {
                Text = "Stopwatch",
                Font = new Font("Segoe UI", 22F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(0, 4)
            };
            stopwatchView.Controls.Add(title);

            swDisplay = new Label {
                Text = "00:00:00.0",
                Font = new Font("Segoe UI", 48F, FontStyle.Bold),
                AutoSize = false,
                Size = new Size(500, 90),
                Location = new Point(30, 130),
                TextAlign = ContentAlignment.MiddleCenter
            };
            stopwatchView.Controls.Add(swDisplay);

            swStartBtn = new Button {
                Text = "Start",
                Size = new Size(120, 42),
                Location = new Point(155, 250),
                FlatStyle = FlatStyle.Flat,
                BackColor = accentBlue,
                ForeColor = textWhite,
                Font = new Font("Segoe UI Semibold", 11F),
                Cursor = Cursors.Hand
            };
            swStartBtn.FlatAppearance.BorderSize = 0;
            swStartBtn.Click += (s, e) => {
                if (stopwatch.IsRunning) { stopwatch.Stop(); swStartBtn.Text = "Resume"; }
                else { stopwatch.Start(); swStartBtn.Text = "Pause"; }
            };

            Button swResetBtn = new Button {
                Text = "Reset",
                Size = new Size(120, 42),
                Location = new Point(290, 250),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgCard,
                ForeColor = textWhite,
                Font = new Font("Segoe UI Semibold", 11F),
                Cursor = Cursors.Hand
            };
            swResetBtn.FlatAppearance.BorderSize = 0;
            swResetBtn.Click += (s, e) => {
                stopwatch.Reset();
                swDisplay.Text = "00:00:00.0";
                swStartBtn.Text = "Start";
            };

            stopwatchView.Controls.Add(swStartBtn);
            stopwatchView.Controls.Add(swResetBtn);
            mainArea.Controls.Add(stopwatchView);
        }

        private void SwitchView(string view)
        {
            if (view == "alarms") {
                alarmsView.Visible = true;
                stopwatchView.Visible = false;
                navAlarmsBtn.BackColor = accentBlue;
                navStopwatchBtn.BackColor = bgSidebar;
            } else {
                alarmsView.Visible = false;
                stopwatchView.Visible = true;
                navAlarmsBtn.BackColor = bgSidebar;
                navStopwatchBtn.BackColor = accentBlue;
            }
        }

        private void RenderAlarms()
        {
            alarmsListPanel.SuspendLayout();
            alarmsListPanel.Controls.Clear();

            alarms.Sort((a, b) => string.Compare(a.Time, b.Time));

            int cardWidth = Math.Max(460, alarmsListPanel.ClientSize.Width - 22);

            foreach (var alarm in alarms)
            {
                Panel card = new Panel {
                    Size = new Size(cardWidth, 88),
                    BackColor = bgCard,
                    Margin = new Padding(0, 0, 0, 10)
                };

                Label timeLbl = new Label {
                    Text = alarm.Time,
                    Font = new Font("Segoe UI", 24F, FontStyle.Bold),
                    ForeColor = alarm.Enabled ? textWhite : Color.Gray,
                    Location = new Point(16, 8),
                    AutoSize = true
                };

                Label nameLbl = new Label {
                    Text = alarm.Label,
                    Font = new Font("Segoe UI Semibold", 12F),
                    ForeColor = alarm.Enabled ? textWhite : Color.Gray,
                    Location = new Point(125, 19),
                    AutoSize = true
                };

                string songName = string.IsNullOrEmpty(alarm.AudioPath) ? "Default Chime" : Path.GetFileName(alarm.AudioPath);
                string daysText = string.IsNullOrEmpty(alarm.Days) ? "Once" : alarm.Days;

                Label metaLbl = new Label {
                    Text = daysText + "   •   ♪ " + songName,
                    Font = new Font("Segoe UI", 9.5F),
                    ForeColor = textMuted,
                    Location = new Point(18, 56),
                    AutoSize = true
                };

                CheckBox toggle = new CheckBox {
                    Text = alarm.Enabled ? "ON" : "OFF",
                    Checked = alarm.Enabled,
                    Appearance = Appearance.Button,
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(58, 32),
                    Location = new Point(cardWidth - 190, 28),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    BackColor = alarm.Enabled ? accentBlue : Color.FromArgb(65, 65, 65),
                    ForeColor = textWhite,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                toggle.FlatAppearance.BorderSize = 0;
                toggle.CheckedChanged += (s, e) => {
                    alarm.Enabled = toggle.Checked;
                    SaveAlarms();
                    RenderAlarms();
                };

                Button editBtn = new Button {
                    Text = "Edit",
                    Size = new Size(60, 32),
                    Location = new Point(cardWidth - 122, 28),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(65, 65, 65),
                    ForeColor = textWhite,
                    Cursor = Cursors.Hand
                };
                editBtn.FlatAppearance.BorderSize = 0;
                editBtn.Click += (s, e) => OpenAlarmEditor(alarm);

                Button delBtn = new Button {
                    Text = "✕",
                    Size = new Size(40, 32),
                    Location = new Point(cardWidth - 54, 28),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(65, 65, 65),
                    ForeColor = Color.FromArgb(255, 110, 110),
                    Cursor = Cursors.Hand
                };
                delBtn.FlatAppearance.BorderSize = 0;
                delBtn.Click += (s, e) => {
                    alarms.Remove(alarm);
                    SaveAlarms();
                    RenderAlarms();
                };

                card.Controls.Add(timeLbl);
                card.Controls.Add(nameLbl);
                card.Controls.Add(metaLbl);
                card.Controls.Add(toggle);
                card.Controls.Add(editBtn);
                card.Controls.Add(delBtn);

                alarmsListPanel.Controls.Add(card);
            }

            alarmsListPanel.ResumeLayout();
        }

        private void OpenAlarmEditor(AlarmItem existing)
        {
            Form dlg = new Form {
                Text = existing == null ? "Add New Alarm" : "Edit Alarm",
                Size = new Size(410, 440),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = bgDark,
                ForeColor = textWhite,
                Font = new Font("Segoe UI", 10F)
            };

            string[] parts = (existing != null ? existing.Time : "07:00").Split(':');

            ComboBox hourBox = new ComboBox { Location = new Point(105, 25), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 18F, FontStyle.Bold), BackColor = bgCard, ForeColor = textWhite };
            ComboBox minBox = new ComboBox { Location = new Point(210, 25), Width = 80, DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Segoe UI", 18F, FontStyle.Bold), BackColor = bgCard, ForeColor = textWhite };
            for (int h = 0; h < 24; h++) hourBox.Items.Add(h.ToString("00"));
            for (int m = 0; m < 60; m++) minBox.Items.Add(m.ToString("00"));
            hourBox.SelectedItem = parts[0];
            minBox.SelectedItem = parts[1];

            Label colon = new Label { Text = ":", Font = new Font("Segoe UI", 20F, FontStyle.Bold), Location = new Point(191, 25), AutoSize = true };

            Label lblTitle = new Label { Text = "Alarm Name", Location = new Point(30, 85), AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
            TextBox nameBox = new TextBox { Location = new Point(30, 110), Width = 335, Text = existing != null ? existing.Label : "Wake Up", BackColor = bgCard, ForeColor = textWhite, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Segoe UI", 11F) };

            Label daysTitle = new Label { Text = "Repeat Days", Location = new Point(30, 155), AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
            string[] dayNames = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            List<CheckBox> dayChecks = new List<CheckBox>();
            string currentDays = existing != null ? existing.Days : "Mon, Tue, Wed, Thu, Fri";

            for (int i = 0; i < 7; i++)
            {
                CheckBox cb = new CheckBox {
                    Text = dayNames[i],
                    Location = new Point(30 + (i * 48), 182),
                    Size = new Size(46, 30),
                    Appearance = Appearance.Button,
                    FlatStyle = FlatStyle.Flat,
                    TextAlign = ContentAlignment.MiddleCenter,
                    Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                    Checked = currentDays.Contains(dayNames[i]),
                    BackColor = currentDays.Contains(dayNames[i]) ? accentBlue : bgCard,
                    ForeColor = textWhite
                };
                cb.FlatAppearance.BorderSize = 0;
                cb.CheckedChanged += (s, e) => cb.BackColor = cb.Checked ? accentBlue : bgCard;
                dayChecks.Add(cb);
                dlg.Controls.Add(cb);
            }

            Label audioTitle = new Label { Text = "Custom Music File (MP3 / WAV)", Location = new Point(30, 230), AutoSize = true, Font = new Font("Segoe UI Semibold", 10F) };
            string selectedAudio = existing != null ? existing.AudioPath : "";
            Label audioPathLbl = new Label {
                Text = string.IsNullOrEmpty(selectedAudio) ? "Default System Chime" : Path.GetFileName(selectedAudio),
                Location = new Point(30, 260),
                Size = new Size(235, 28),
                BackColor = bgCard,
                ForeColor = textMuted,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Button browseBtn = new Button {
                Text = "Browse...",
                Location = new Point(275, 258),
                Size = new Size(90, 32),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(65, 65, 65),
                ForeColor = textWhite
            };
            browseBtn.FlatAppearance.BorderSize = 0;
            browseBtn.Click += (s, e) => {
                OpenFileDialog ofd = new OpenFileDialog {
                    Title = "Select Alarm Music File",
                    Filter = "Audio Files (*.mp3;*.wav;*.wma)|*.mp3;*.wav;*.wma|All Files (*.*)|*.*"
                };
                if (ofd.ShowDialog() == DialogResult.OK) {
                    selectedAudio = ofd.FileName;
                    audioPathLbl.Text = Path.GetFileName(selectedAudio);
                }
            };

            Button saveBtn = new Button {
                Text = "Save Alarm",
                Location = new Point(30, 330),
                Size = new Size(160, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = accentBlue,
                ForeColor = textWhite,
                Font = new Font("Segoe UI Semibold", 10.5F)
            };
            saveBtn.FlatAppearance.BorderSize = 0;
            saveBtn.Click += (s, e) => {
                List<string> chosenDays = new List<string>();
                foreach (var cb in dayChecks) if (cb.Checked) chosenDays.Add(cb.Text);

                if (existing == null) {
                    alarms.Add(new AlarmItem {
                        Id = Guid.NewGuid().ToString(),
                        Time = hourBox.SelectedItem.ToString() + ":" + minBox.SelectedItem.ToString(),
                        Label = string.IsNullOrWhiteSpace(nameBox.Text) ? "Alarm" : nameBox.Text.Trim(),
                        Days = string.Join(", ", chosenDays),
                        AudioPath = selectedAudio,
                        Enabled = true
                    });
                } else {
                    existing.Time = hourBox.SelectedItem.ToString() + ":" + minBox.SelectedItem.ToString();
                    existing.Label = string.IsNullOrWhiteSpace(nameBox.Text) ? "Alarm" : nameBox.Text.Trim();
                    existing.Days = string.Join(", ", chosenDays);
                    existing.AudioPath = selectedAudio;
                    existing.Enabled = true;
                }
                SaveAlarms();
                RenderAlarms();
                dlg.Close();
            };

            Button cancelBtn = new Button {
                Text = "Cancel",
                Location = new Point(205, 330),
                Size = new Size(160, 40),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgCard,
                ForeColor = textWhite
            };
            cancelBtn.FlatAppearance.BorderSize = 0;
            cancelBtn.Click += (s, e) => dlg.Close();

            dlg.Controls.AddRange(new Control[] { hourBox, colon, minBox, lblTitle, nameBox, daysTitle, audioTitle, audioPathLbl, browseBtn, saveBtn, cancelBtn });
            dlg.ShowDialog(this);
        }

        private void ClockTimer_Tick(object sender, EventArgs e)
        {
            DateTime now = DateTime.Now;
            liveTimeLabel.Text = now.ToString("HH:mm:ss");
            liveDateLabel.Text = now.ToString("dddd, MMM dd");

            if (stopwatch.IsRunning)
            {
                TimeSpan ts = stopwatch.Elapsed;
                swDisplay.Text = string.Format("{0:00}:{1:00}:{2:00}.{3:0}", ts.Hours, ts.Minutes, ts.Seconds, ts.Milliseconds / 100);
            }

            string hm = now.ToString("HH:mm");
            string dayShort = now.ToString("ddd");

            if (now.Second == 0)
            {
                foreach (var alarm in alarms.ToArray())
                {
                    if (!alarm.Enabled) continue;
                    string key = alarm.Id + "_" + hm;
                    if (alarm.Time == hm && !triggeredThisMinute.Contains(key))
                    {
                        if (string.IsNullOrEmpty(alarm.Days) || alarm.Days.Contains(dayShort))
                        {
                            triggeredThisMinute.Add(key);
                            TriggerAlarm(alarm);
                        }
                    }
                }
            }
            else if (now.Second > 2)
            {
                triggeredThisMinute.Clear();
            }
        }

        private void TriggerAlarm(AlarmItem alarm)
        {
            if (isRinging) return;
            isRinging = true;

            if (string.IsNullOrEmpty(alarm.Days))
            {
                alarm.Enabled = false;
                SaveAlarms();
                RenderAlarms();
            }

            bool playingCustom = false;
            if (!string.IsNullOrEmpty(alarm.AudioPath) && File.Exists(alarm.AudioPath))
            {
                try {
                    mciSendString("close AlarmMusic", null, 0, IntPtr.Zero);
                    mciSendString("open \"" + alarm.AudioPath + "\" type mpegvideo alias AlarmMusic", null, 0, IntPtr.Zero);
                    mciSendString("play AlarmMusic repeat", null, 0, IntPtr.Zero);
                    playingCustom = true;
                } catch { playingCustom = false; }
            }

            Timer beepTimer = new Timer { Interval = 1000 };
            if (!playingCustom)
            {
                System.Media.SystemSounds.Exclamation.Play();
                beepTimer.Tick += (s, e) => System.Media.SystemSounds.Exclamation.Play();
                beepTimer.Start();
            }

            Form ringDlg = new Form {
                Text = "Alarm",
                Size = new Size(370, 250),
                FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterScreen,
                TopMost = true,
                MaximizeBox = false,
                MinimizeBox = false,
                BackColor = bgDark,
                ForeColor = textWhite
            };

            Label tLbl = new Label { Text = alarm.Time, Font = new Font("Segoe UI", 34F, FontStyle.Bold), Dock = DockStyle.Top, Height = 75, TextAlign = ContentAlignment.BottomCenter };
            Label nLbl = new Label { Text = alarm.Label, Font = new Font("Segoe UI Semibold", 14F), Dock = DockStyle.Top, Height = 35, TextAlign = ContentAlignment.MiddleCenter };

            Button snoozeBtn = new Button {
                Text = "Snooze (5m)",
                Size = new Size(140, 42),
                Location = new Point(30, 145),
                FlatStyle = FlatStyle.Flat,
                BackColor = bgCard,
                ForeColor = textWhite,
                Font = new Font("Segoe UI Semibold", 10.5F)
            };
            snoozeBtn.FlatAppearance.BorderSize = 0;

            Button dismissBtn = new Button {
                Text = "Dismiss",
                Size = new Size(140, 42),
                Location = new Point(185, 145),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(215, 35, 35),
                ForeColor = textWhite,
                Font = new Font("Segoe UI Semibold", 10.5F)
            };
            dismissBtn.FlatAppearance.BorderSize = 0;

            Action stopSound = () => {
                beepTimer.Stop();
                mciSendString("close AlarmMusic", null, 0, IntPtr.Zero);
                isRinging = false;
            };

            snoozeBtn.Click += (s, e) => {
                stopSound();
                alarms.Add(new AlarmItem {
                    Id = Guid.NewGuid().ToString(),
                    Time = DateTime.Now.AddMinutes(5).ToString("HH:mm"),
                    Label = alarm.Label + " (Snoozed)",
                    Days = "",
                    AudioPath = alarm.AudioPath,
                    Enabled = true
                });
                SaveAlarms();
                RenderAlarms();
                ringDlg.Close();
            };

            dismissBtn.Click += (s, e) => {
                stopSound();
                ringDlg.Close();
            };

            ringDlg.FormClosed += (s, e) => stopSound();
            ringDlg.Controls.AddRange(new Control[] { nLbl, tLbl, snoozeBtn, dismissBtn });
            ringDlg.Show();
        }

        private void LoadAlarms()
        {
            alarms.Clear();
            if (File.Exists(dataFile))
            {
                foreach (var line in File.ReadAllLines(dataFile))
                {
                    var p = line.Split('|');
                    if (p.Length >= 6)
                    {
                        alarms.Add(new AlarmItem {
                            Id = p[0], Time = p[1], Label = p[2], Days = p[3], AudioPath = p[4], Enabled = p[5] == "1"
                        });
                    }
                }
            }
            else
            {
                alarms.Add(new AlarmItem {
                    Id = Guid.NewGuid().ToString(),
                    Time = "07:00",
                    Label = "Good Morning",
                    Days = "Mon, Tue, Wed, Thu, Fri",
                    AudioPath = "",
                    Enabled = true
                });
            }
        }

        private void SaveAlarms()
        {
            List<string> lines = new List<string>();
            foreach (var a in alarms)
            {
                lines.Add(string.Join("|", a.Id, a.Time, a.Label.Replace("|", ""), a.Days, a.AudioPath, a.Enabled ? "1" : "0"));
            }
            File.WriteAllLines(dataFile, lines);
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
