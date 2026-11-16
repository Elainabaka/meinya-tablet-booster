using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace MeinyaTabletBooster
{
    public sealed class DoubleBufferedCanvas : Panel
    {
        public DoubleBufferedCanvas()
        {
            this.DoubleBuffered = true;
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            this.UpdateStyles();
        }
    }

    public sealed class MainForm : Form
    {
        private readonly TabletDatabaseRoot _db;
        private DetectedHardwareState _hwState;
        private readonly TemporalResamplerEngine _engine;
        private readonly CanvasSamplePoint[] _canvasRenderScratch = new CanvasSamplePoint[TemporalResamplerEngine.CanvasRingCapacity];
        private readonly float _dpiScale;

        private TabletEntry _selectedTablet;
        private Image _mascotImg;
        private Image _warnImg;
        private System.Windows.Forms.Timer _uiTimer;

        // Header controls
        private PictureBox _picHeaderMascot;
        private Label _lblAppTitle;
        private Label _lblDetectedSummary;
        private Label _lblProfilePickerCaption;
        private ComboBox _cmbTabletProfile;
        private Button _btnRescanUsb;

        // Smart safety & sweet spot banner
        private Panel _pnlSafetyBanner;
        private PictureBox _picSafetyIcon;
        private Label _lblSafetyBadgeTitle;
        private Label _lblSafetyDetail;

        // 4 Telemetry & Memory cards
        private Label _lblRawHzVal;
        private Label _lblRawHzSub;
        private Label _lblOutHzVal;
        private Label _lblOutHzSub;
        private Label _lblIntervalVal;
        private Label _lblIntervalSub;
        private Label _lblMemoryVal;
        private Label _lblMemorySub;

        // Left Card: Temporal Resampler
        private ComboBox _cmbTargetHz;
        private TrackBar _trkSmoothing;
        private Label _lblSmoothingValue;
        private TextBox _lblTabletNoteBox;
        private Button _btnToggleResampler;
        private Button _btnCleanMemory;

        // Right Card: Wacom Driver Config
        private ComboBox _cmbPacketRate;
        private CheckBox _chkMaxDataRate;
        private CheckBox _chkWindowsInk;
        private CheckBox _chkFastTap;
        private Button _btnSaveWacomConfig;
        private Button _btnResetOriginal;
        private Button _btnRestartServiceAdmin;
        private Label _lblWacomStatus;

        // Bottom Area: Switchable Canvas / Glossary & Memory Guide
        private Button _btnTabCanvas;
        private Button _btnTabGlossary;
        private DoubleBufferedCanvas _pnlCanvas;
        private TextBox _txtGlossaryAndMemory;

        // Footer: Author Elainabaka & Star CTA
        private Label _lblFooterAuthor;
        private Button _btnStarGithub;

        private sealed class TargetHzOption
        {
            public int Hz { get; set; }
            public string Label { get; set; }
            public override string ToString() { return Label; }
        }

        private sealed class TabletComboItem
        {
            public TabletEntry Entry { get; set; }
            public bool IsDetected { get; set; }
            public override string ToString()
            {
                string prefix = IsDetected ? "[Đã nhận diện] " : "";
                return prefix + Entry.name + " (" + Entry.NativeHzDisplay + ")";
            }
        }

        public MainForm()
        {
            using (Graphics g = this.CreateGraphics())
            {
                _dpiScale = g.DpiX / 96.0f;
            }
            if (_dpiScale < 1.0f) _dpiScale = 1.0f;

            _db = TabletDatabaseManager.LoadDatabase();
            _hwState = HardwareDetectorAndAdvisor.DetectConnectedTablet(_db);
            _selectedTablet = _hwState.Tablet;
            _engine = new TemporalResamplerEngine();
            _engine.HasTabletContext = _hwState.IsConnected || File.Exists(WacomConfigManager.DefaultDatPath);

            LoadMascotImages();
            InitializeLayout();
            PopulateTabletSelector();
            PopulateTargetHzOptions();
            LoadCurrentWacomConfigIntoUi();
            PopulateGlossaryText();
            UpdateSafetyBanner();

            _engine.StartHookAndEngine();

            _uiTimer = new System.Windows.Forms.Timer { Interval = 100 };
            _uiTimer.Tick += OnUiTimerTick;
            _uiTimer.Start();
        }

        private int S(int logicalPx)
        {
            return (int)Math.Round(logicalPx * _dpiScale);
        }

        /// <summary>
        /// INV-6: Dùng GraphicsUnit.Pixel đã nhân tỷ lệ _dpiScale để Windows không tự phóng chữ lần 2 ở màn hình 125%-150%.
        /// </summary>
        private Font MakeFont(float logicalPx, FontStyle style = FontStyle.Regular)
        {
            return new Font("Segoe UI", logicalPx * _dpiScale, style, GraphicsUnit.Pixel);
        }

        private void LoadMascotImages()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string mascotPath = Path.Combine(baseDir, "assets", "meinya_mascot.png");
            string warnPath = Path.Combine(baseDir, "assets", "meinya_warn.png");
            string icoPath = Path.Combine(baseDir, "assets", "app.ico");

            try
            {
                if (File.Exists(icoPath))
                {
                    this.Icon = new Icon(icoPath);
                }
            }
            catch { }

            try
            {
                if (File.Exists(mascotPath))
                {
                    using (FileStream fs = new FileStream(mascotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        _mascotImg = Image.FromStream(fs);
                    }
                }
            }
            catch { }

            try
            {
                if (File.Exists(warnPath))
                {
                    using (FileStream fs = new FileStream(warnPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        _warnImg = Image.FromStream(fs);
                    }
                }
            }
            catch { }
        }

        private void InitializeLayout()
        {
            this.AutoScaleMode = AutoScaleMode.None;
            this.Text = "Meinya Tablet Booster — Đo sự kiện chuột & Làm mượt con trỏ | by Elainabaka";
            this.ClientSize = new Size(S(980), S(740));
            this.MinimumSize = new Size(S(960), S(720));
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(14, 18, 28);
            this.ForeColor = Color.FromArgb(236, 242, 250);
            this.Font = MakeFont(12.5f, FontStyle.Regular);
            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.F8)
                {
                    ToggleResamplerState();
                    e.Handled = true;
                }
            };

            // =========================================================================
            // 1. TOP HEADER BAR (Y = 0 .. 68)
            // =========================================================================
            Panel pnlHeader = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(S(980), S(68)),
                BackColor = Color.FromArgb(20, 27, 42)
            };
            this.Controls.Add(pnlHeader);

            _picHeaderMascot = new PictureBox
            {
                Location = new Point(S(14), S(6)),
                Size = new Size(S(56), S(56)),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = _mascotImg
            };
            pnlHeader.Controls.Add(_picHeaderMascot);

            _lblAppTitle = new Label
            {
                Text = "MEINYA TABLET BOOSTER — LÀM MƯỢT CON TRỎ & BỘ NHỚ ĐỆM",
                Font = MakeFont(14.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(110, 205, 255),
                Location = new Point(S(78), S(6)),
                Size = new Size(S(570), S(22)),
                UseMnemonic = false
            };
            pnlHeader.Controls.Add(_lblAppTitle);

            _lblDetectedSummary = new Label
            {
                Text = "",
                Font = MakeFont(10.6f, FontStyle.Regular),
                ForeColor = Color.FromArgb(180, 196, 218),
                Location = new Point(S(78), S(29)),
                Size = new Size(S(570), S(36)),
                UseMnemonic = false
            };
            pnlHeader.Controls.Add(_lblDetectedSummary);

            _lblProfilePickerCaption = new Label
            {
                Text = "Hồ sơ Bảng vẽ (Tự nhận diện / Tra cứu CSDL):",
                Font = MakeFont(11.0f, FontStyle.Bold),
                ForeColor = Color.FromArgb(165, 185, 215),
                Location = new Point(S(655), S(10)),
                Size = new Size(S(310), S(18)),
                UseMnemonic = false
            };
            pnlHeader.Controls.Add(_lblProfilePickerCaption);

            _cmbTabletProfile = new ComboBox
            {
                Location = new Point(S(655), S(32)),
                Size = new Size(S(232), S(26)),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                Font = MakeFont(11.5f, FontStyle.Regular)
            };
            _cmbTabletProfile.SelectedIndexChanged += OnTabletProfileChanged;
            pnlHeader.Controls.Add(_cmbTabletProfile);

            _btnRescanUsb = new Button
            {
                Text = "Quét lại",
                Font = MakeFont(10.8f, FontStyle.Bold),
                BackColor = Color.FromArgb(42, 62, 96),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(893), S(32)),
                Size = new Size(S(72), S(26)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnRescanUsb.FlatAppearance.BorderSize = 0;
            _btnRescanUsb.Click += OnRescanUsbClicked;
            pnlHeader.Controls.Add(_btnRescanUsb);

            // =========================================================================
            // 2. SMART SAFETY & SWEET-SPOT BANNER (Y = 74 .. 134, H = 60)
            // =========================================================================
            _pnlSafetyBanner = new Panel
            {
                Location = new Point(S(14), S(74)),
                Size = new Size(S(952), S(60)),
                BackColor = Color.FromArgb(18, 46, 36)
            };
            this.Controls.Add(_pnlSafetyBanner);

            _picSafetyIcon = new PictureBox
            {
                Location = new Point(S(10), S(6)),
                Size = new Size(S(48), S(48)),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                Image = _mascotImg
            };
            _pnlSafetyBanner.Controls.Add(_picSafetyIcon);

            _lblSafetyBadgeTitle = new Label
            {
                Text = "ĐIỂM NGỌT TỐI ƯU 2X MÀN HÌNH",
                Font = MakeFont(12.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(95, 240, 155),
                Location = new Point(S(66), S(6)),
                Size = new Size(S(876), S(20)),
                UseMnemonic = false
            };
            _pnlSafetyBanner.Controls.Add(_lblSafetyBadgeTitle);

            _lblSafetyDetail = new Label
            {
                Text = "",
                Font = MakeFont(10.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(215, 232, 225),
                Location = new Point(S(66), S(26)),
                Size = new Size(S(876), S(32)),
                UseMnemonic = false
            };
            _pnlSafetyBanner.Controls.Add(_lblSafetyDetail);

            // =========================================================================
            // 3. FOUR TELEMETRY & MEMORY CARDS (Y = 140 .. 218, H = 78)
            // =========================================================================
            int cardW = S(230);
            int cardGap = S(10);
            int cardY = S(140);
            int cardH = S(78);

            CreateTelemetryCard(
                S(14), cardY, cardW, cardH,
                "NHỊP SỰ KIỆN CHUỘT (HOOK)",
                "0 Hz",
                "Hồ sơ tham khảo: 133 Hz (ước lượng)",
                Color.FromArgb(80, 230, 145),
                out _lblRawHzVal, out _lblRawHzSub);

            CreateTelemetryCard(
                S(14) + (cardW + cardGap), cardY, cardW, cardH,
                "NHỊP CON TRỎ ƯỚC TÍNH",
                "0 Hz",
                "Không phải Hz bút WinTab/Ink",
                Color.FromArgb(88, 196, 255),
                out _lblOutHzVal, out _lblOutHzSub);

            CreateTelemetryCard(
                S(14) + (cardW + cardGap) * 2, cardY, cardW, cardH,
                "CHU KỲ NHẬN DIỆN (INTERVAL)",
                "-- ms",
                "Đỉnh đo được (Peak): 0 Hz",
                Color.FromArgb(255, 198, 92),
                out _lblIntervalVal, out _lblIntervalSub);

            CreateTelemetryCard(
                S(14) + (cardW + cardGap) * 3, cardY, S(952) - (cardW + cardGap) * 3, cardH,
                "BỘ NHỚ ĐỆM VÒNG (RING BUFFER)",
                "Dung lượng cố định",
                "Đệm: 0/768 ô | Tự xả sau 180ms",
                Color.FromArgb(196, 148, 255),
                out _lblMemoryVal, out _lblMemorySub);

            // =========================================================================
            // 4. LEFT & RIGHT CONTROL CARDS (Y = 224 .. 476, H = 252)
            // =========================================================================
            int midY = S(224);
            int midH = S(252);
            int halfW = S(470);

            // --- LEFT CARD: SOFTWARE TEMPORAL RESAMPLER & MEMORY CLEANUP ---
            Panel pnlLeft = CreateSectionCard(
                S(14), midY, halfW, midH,
                "1. Làm Mượt Con Trỏ Chuột & Thu Dọn Đệm");
            this.Controls.Add(pnlLeft);

            Label lblTargetCaption = new Label
            {
                Text = "Nhịp chèn chuột mục tiêu (tham khảo 1x/2x màn hình):",
                Font = MakeFont(11.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(215, 225, 240),
                Location = new Point(S(14), S(36)),
                Size = new Size(S(442), S(18)),
                UseMnemonic = false
            };
            pnlLeft.Controls.Add(lblTargetCaption);

            _cmbTargetHz = new ComboBox
            {
                Location = new Point(S(14), S(57)),
                Size = new Size(S(442), S(26)),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                Font = MakeFont(11.8f, FontStyle.Regular)
            };
            _cmbTargetHz.SelectedIndexChanged += OnTargetHzOrSmoothingChanged;
            pnlLeft.Controls.Add(_cmbTargetHz);

            Label lblSmoothTitle = new Label
            {
                Text = "Độ bám quỹ đạo (Interpolation):",
                Font = MakeFont(11.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(215, 225, 240),
                Location = new Point(S(14), S(90)),
                Size = new Size(S(220), S(18)),
                UseMnemonic = false
            };
            pnlLeft.Controls.Add(lblSmoothTitle);

            _lblSmoothingValue = new Label
            {
                Text = "35% (Cân bằng — Khuyên dùng)",
                Font = MakeFont(11.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(95, 200, 255),
                Location = new Point(S(236), S(90)),
                Size = new Size(S(220), S(18)),
                TextAlign = ContentAlignment.MiddleRight,
                UseMnemonic = false
            };
            pnlLeft.Controls.Add(_lblSmoothingValue);

            _trkSmoothing = new TrackBar
            {
                Location = new Point(S(10), S(109)),
                Size = new Size(S(450), S(24)),
                Minimum = 0,
                Maximum = 85,
                TickFrequency = 5,
                Value = 35,
                AutoSize = false
            };
            _trkSmoothing.ValueChanged += OnTargetHzOrSmoothingChanged;
            pnlLeft.Controls.Add(_trkSmoothing);

            _lblTabletNoteBox = new TextBox
            {
                Text = "",
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = MakeFont(10.0f, FontStyle.Regular),
                ForeColor = Color.FromArgb(188, 208, 232),
                BackColor = Color.FromArgb(16, 22, 34),
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(S(14), S(140)),
                Size = new Size(S(442), S(56))
            };
            pnlLeft.Controls.Add(_lblTabletNoteBox);

            _btnToggleResampler = new Button
            {
                Text = "BẬT LÀM MƯỢT CON TRỎ (F8)",
                Font = MakeFont(12.2f, FontStyle.Bold),
                BackColor = Color.FromArgb(28, 158, 92),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(14), S(203)),
                Size = new Size(S(292), S(38)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnToggleResampler.FlatAppearance.BorderSize = 0;
            _btnToggleResampler.Click += (s, e) => ToggleResamplerState();
            pnlLeft.Controls.Add(_btnToggleResampler);

            _btnCleanMemory = new Button
            {
                Text = "Dọn Đệm & Xả RAM",
                Font = MakeFont(11.2f, FontStyle.Bold),
                BackColor = Color.FromArgb(62, 76, 108),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(314), S(203)),
                Size = new Size(S(142), S(38)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnCleanMemory.FlatAppearance.BorderSize = 0;
            _btnCleanMemory.Click += OnCleanMemoryClicked;
            pnlLeft.Controls.Add(_btnCleanMemory);

            // --- RIGHT CARD: WACOM DRIVER CONFIG & ATOMIC RESTORE ---
            Panel pnlRight = CreateSectionCard(
                S(496), midY, halfW, midH,
                "2. Cấu Hình Wacom Driver (Wacom_Tablet.dat) & Khôi Phục Gốc");
            this.Controls.Add(pnlRight);

            Label lblPktRate = new Label
            {
                Text = "PacketRate (đơn vị chưa công bố):",
                Font = MakeFont(11.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(215, 225, 240),
                Location = new Point(S(14), S(38)),
                Size = new Size(S(268), S(22)),
                UseMnemonic = false
            };
            pnlRight.Controls.Add(lblPktRate);

            _cmbPacketRate = new ComboBox
            {
                Location = new Point(S(284), S(36)),
                Size = new Size(S(172), S(26)),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(28, 38, 56),
                ForeColor = Color.White,
                Font = MakeFont(11.2f, FontStyle.Regular)
            };
            _cmbPacketRate.Items.AddRange(new object[]
            {
                "100",
                "133",
                "200"
            });
            pnlRight.Controls.Add(_cmbPacketRate);

            _chkMaxDataRate = new CheckBox
            {
                Text = "MaxDataRateOn = true (ý nghĩa chưa được hãng công bố)",
                Font = MakeFont(11.3f, FontStyle.Regular),
                ForeColor = Color.FromArgb(218, 226, 238),
                Location = new Point(S(14), S(70)),
                Size = new Size(S(442), S(22)),
                Checked = true,
                UseMnemonic = false
            };
            pnlRight.Controls.Add(_chkMaxDataRate);

            _chkWindowsInk = new CheckBox
            {
                Text = "Bật Windows Ink (Giữ khi viết/vẽ lực nhấn; tắt khi chỉ chơi game)",
                Font = MakeFont(11.3f, FontStyle.Regular),
                ForeColor = Color.FromArgb(218, 226, 238),
                Location = new Point(S(14), S(96)),
                Size = new Size(S(442), S(22)),
                Checked = true,
                UseMnemonic = false
            };
            pnlRight.Controls.Add(_chkWindowsInk);

            _chkFastTap = new CheckBox
            {
                Text = "DoubleClickDistance = 0 (tác dụng cần kiểm tra với driver)",
                Font = MakeFont(11.3f, FontStyle.Regular),
                ForeColor = Color.FromArgb(218, 226, 238),
                Location = new Point(S(14), S(122)),
                Size = new Size(S(442), S(22)),
                Checked = false,
                UseMnemonic = false
            };
            pnlRight.Controls.Add(_chkFastTap);

            _btnSaveWacomConfig = new Button
            {
                Text = "Lưu & Áp Dụng Cấu Hình",
                Font = MakeFont(11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(36, 118, 228),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(14), S(152)),
                Size = new Size(S(216), S(36)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnSaveWacomConfig.FlatAppearance.BorderSize = 0;
            _btnSaveWacomConfig.Click += OnSaveWacomConfigClicked;
            pnlRight.Controls.Add(_btnSaveWacomConfig);

            _btnResetOriginal = new Button
            {
                Text = "Reset Về Cấu Hình Ban Đầu",
                Font = MakeFont(11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(182, 52, 58),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(240), S(152)),
                Size = new Size(S(216), S(36)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnResetOriginal.FlatAppearance.BorderSize = 0;
            _btnResetOriginal.Click += OnResetOriginalClicked;
            pnlRight.Controls.Add(_btnResetOriginal);

            _btnRestartServiceAdmin = new Button
            {
                Text = WacomConfigManager.IsRunningAsAdministrator()
                    ? "Khởi động lại Wacom Service ngay (Đang có Quyền Admin)"
                    : "Mở lại Tool với Quyền Admin (Để tự nạp lại WTabletServicePro)",
                Font = MakeFont(10.8f, FontStyle.Regular),
                BackColor = Color.FromArgb(40, 50, 70),
                ForeColor = Color.FromArgb(195, 208, 228),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(14), S(194)),
                Size = new Size(S(442), S(28)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnRestartServiceAdmin.FlatAppearance.BorderSize = 0;
            _btnRestartServiceAdmin.Click += OnRestartServiceAdminClicked;
            pnlRight.Controls.Add(_btnRestartServiceAdmin);

            _lblWacomStatus = new Label
            {
                Text = "",
                Font = MakeFont(10.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 210, 175),
                Location = new Point(S(14), S(226)),
                Size = new Size(S(442), S(20)),
                UseMnemonic = false
            };
            pnlRight.Controls.Add(_lblWacomStatus);

            // =========================================================================
            // 5. BOTTOM INTERACTIVE AREA: TEST CANVAS & GLOSSARY/MEMORY GUIDE (Y = 482 .. 688)
            // =========================================================================
            Panel pnlBottom = new Panel
            {
                Location = new Point(S(14), S(482)),
                Size = new Size(S(952), S(206)),
                BackColor = Color.FromArgb(20, 27, 40),
                BorderStyle = BorderStyle.FixedSingle
            };
            this.Controls.Add(pnlBottom);

            _btnTabCanvas = new Button
            {
                Text = "1. Bảng Vẽ Kiểm Tra Tốc Độ & Độ Mượt Trực Tiếp (512 Điểm Vòng Tròn)",
                Font = MakeFont(11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(36, 118, 228),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(8), S(6)),
                Size = new Size(S(462), S(28)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnTabCanvas.FlatAppearance.BorderSize = 0;
            _btnTabCanvas.Click += (s, e) => SwitchBottomTab(false);
            pnlBottom.Controls.Add(_btnTabCanvas);

            _btnTabGlossary = new Button
            {
                Text = "2. Bảng Chú Thích Thuật Ngữ & Cơ Chế Thu Dọn Bộ Nhớ 3 Tầng",
                Font = MakeFont(11.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(34, 44, 64),
                ForeColor = Color.FromArgb(190, 205, 228),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(478), S(6)),
                Size = new Size(S(464), S(28)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnTabGlossary.FlatAppearance.BorderSize = 0;
            _btnTabGlossary.Click += (s, e) => SwitchBottomTab(true);
            pnlBottom.Controls.Add(_btnTabGlossary);

            _pnlCanvas = new DoubleBufferedCanvas
            {
                Location = new Point(S(8), S(38)),
                Size = new Size(S(934), S(160)),
                BackColor = Color.FromArgb(11, 15, 22),
                Cursor = Cursors.Cross
            };
            _pnlCanvas.MouseMove += OnCanvasMouseMove;
            _pnlCanvas.Paint += OnCanvasPaint;
            pnlBottom.Controls.Add(_pnlCanvas);

            _txtGlossaryAndMemory = new TextBox
            {
                Location = new Point(S(8), S(38)),
                Size = new Size(S(934), S(160)),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = Color.FromArgb(13, 18, 28),
                ForeColor = Color.FromArgb(222, 232, 245),
                BorderStyle = BorderStyle.None,
                Font = MakeFont(11.5f, FontStyle.Regular),
                Visible = false
            };
            pnlBottom.Controls.Add(_txtGlossaryAndMemory);

            // =========================================================================
            // 6. AUTHOR & GITHUB STAR FOOTER BAR (Y = 694 .. 734)
            // =========================================================================
            Panel pnlFooter = new Panel
            {
                Location = new Point(S(14), S(694)),
                Size = new Size(S(952), S(38)),
                BackColor = Color.FromArgb(22, 30, 46)
            };
            this.Controls.Add(pnlFooter);

            _lblFooterAuthor = new Label
            {
                Text = "Tác giả: Elainabaka | Mascot: Meinya — Mong được mọi người ủng hộ! Thấy hữu ích hãy tặng repo 1 Star trên GitHub nhé!",
                Font = MakeFont(10.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(235, 210, 135),
                Location = new Point(S(12), S(9)),
                Size = new Size(S(684), S(20)),
                UseMnemonic = false
            };
            pnlFooter.Controls.Add(_lblFooterAuthor);

            _btnStarGithub = new Button
            {
                Text = "★ Tặng Star Ủng Hộ Elainabaka",
                Font = MakeFont(11.0f, FontStyle.Bold),
                BackColor = Color.FromArgb(212, 145, 28),
                ForeColor = Color.FromArgb(18, 20, 26),
                FlatStyle = FlatStyle.Flat,
                Location = new Point(S(702), S(5)),
                Size = new Size(S(242), S(28)),
                Cursor = Cursors.Hand,
                UseMnemonic = false
            };
            _btnStarGithub.FlatAppearance.BorderSize = 0;
            _btnStarGithub.Click += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/Elainabaka",
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            pnlFooter.Controls.Add(_btnStarGithub);
        }

        private Panel CreateSectionCard(int x, int y, int w, int h, string headerTitle)
        {
            Panel card = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.FromArgb(20, 27, 40),
                BorderStyle = BorderStyle.FixedSingle
            };

            Panel headerBar = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(w, S(28)),
                BackColor = Color.FromArgb(28, 38, 58)
            };
            card.Controls.Add(headerBar);

            Label lblHeader = new Label
            {
                Text = headerTitle,
                Font = MakeFont(11.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(130, 205, 255),
                Location = new Point(S(10), S(5)),
                Size = new Size(w - S(20), S(19)),
                UseMnemonic = false
            };
            headerBar.Controls.Add(lblHeader);

            return card;
        }

        private void CreateTelemetryCard(
            int x, int y, int w, int h,
            string title, string initVal, string initSub, Color accentColor,
            out Label lblValue, out Label lblSub)
        {
            Panel p = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                BackColor = Color.FromArgb(22, 30, 44),
                BorderStyle = BorderStyle.FixedSingle
            };

            Label lblTitle = new Label
            {
                Text = title,
                Font = MakeFont(10.2f, FontStyle.Bold),
                ForeColor = Color.FromArgb(155, 172, 196),
                Location = new Point(S(10), S(6)),
                Size = new Size(w - S(16), S(16)),
                UseMnemonic = false
            };

            lblValue = new Label
            {
                Text = initVal,
                Font = MakeFont(19.5f, FontStyle.Bold),
                ForeColor = accentColor,
                Location = new Point(S(10), S(23)),
                Size = new Size(w - S(16), S(28)),
                UseMnemonic = false
            };

            lblSub = new Label
            {
                Text = initSub,
                Font = MakeFont(10.0f, FontStyle.Regular),
                ForeColor = Color.FromArgb(135, 152, 175),
                Location = new Point(S(10), S(54)),
                Size = new Size(w - S(16), S(18)),
                UseMnemonic = false
            };

            p.Controls.Add(lblTitle);
            p.Controls.Add(lblValue);
            p.Controls.Add(lblSub);
            this.Controls.Add(p);
        }

        private void PopulateTabletSelector()
        {
            _cmbTabletProfile.Items.Clear();
            int selectedIdx = 0;

            if (_db != null && _db.tablets != null)
            {
                for (int i = 0; i < _db.tablets.Count; i++)
                {
                    TabletEntry entry = _db.tablets[i];
                    bool isDet = (_hwState.Tablet != null && string.Equals(entry.id, _hwState.Tablet.id, StringComparison.OrdinalIgnoreCase));
                    if (isDet) selectedIdx = i;
                    _cmbTabletProfile.Items.Add(new TabletComboItem
                    {
                        Entry = entry,
                        IsDetected = isDet
                    });
                }
            }

            if (_cmbTabletProfile.Items.Count > 0)
            {
                _cmbTabletProfile.SelectedIndex = selectedIdx;
            }
        }

        private void PopulateTargetHzOptions()
        {
            int monHz = _hwState.MonitorRefreshHz > 0 ? _hwState.MonitorRefreshHz : 165;
            int sweet1x = _hwState.SweetSpot1xHz > 0 ? _hwState.SweetSpot1xHz : monHz;
            int sweet2x = _hwState.SweetSpot2xHz > 0 ? _hwState.SweetSpot2xHz : (monHz * 2);

            _cmbTargetHz.Items.Clear();
            List<TargetHzOption> options = new List<TargetHzOption>
            {
                new TargetHzOption
                {
                    Hz = sweet2x,
                    Label = sweet2x + " Hz — Nhịp mục tiêu 2x màn hình " + monHz + "Hz (Thử độ mượt)"
                },
                new TargetHzOption
                {
                    Hz = sweet1x,
                    Label = sweet1x + " Hz — Nhịp mục tiêu 1x màn hình " + monHz + "Hz (Tham khảo)"
                }
            };

            int[] extraCandidates = new int[] { 200, 240, 360, 420, 500 };
            foreach (int hz in extraCandidates)
            {
                if (Math.Abs(hz - sweet1x) <= 5 || Math.Abs(hz - sweet2x) <= 8) continue;
                string suffix;
                if (hz <= 360) suffix = " (Ngưỡng phần mềm ước lượng)";
                else if (hz <= 450) suffix = " (Nhịp cao — Cần tự thử)";
                else suffix = " (CẢNH BÁO: Nhịp mục tiêu cao)";

                options.Add(new TargetHzOption
                {
                    Hz = hz,
                    Label = hz + " Hz" + suffix
                });
            }

            foreach (TargetHzOption opt in options)
            {
                _cmbTargetHz.Items.Add(opt);
            }
            _cmbTargetHz.SelectedIndex = 0;
        }

        private void OnRescanUsbClicked(object sender, EventArgs e)
        {
            if (!_btnRescanUsb.Enabled) return;
            _btnRescanUsb.Enabled = false;
            _btnRescanUsb.Text = "Quét...";
            ThreadPool.QueueUserWorkItem(_ =>
            {
                DetectedHardwareState detected = HardwareDetectorAndAdvisor.DetectConnectedTablet(_db);
                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            _hwState = detected;
                            _selectedTablet = _hwState.Tablet;
                            _engine.HasTabletContext = _hwState.IsConnected || File.Exists(WacomConfigManager.DefaultDatPath);
                            PopulateTabletSelector();
                            PopulateTargetHzOptions();
                            UpdateHeaderAndTabletNotes();
                            UpdateSafetyBanner();
                            _btnRescanUsb.Text = "Quét lại";
                            _btnRescanUsb.Enabled = true;
                        });
                    }
                }
                catch { }
            });
        }

        private void OnTabletProfileChanged(object sender, EventArgs e)
        {
            TabletComboItem item = _cmbTabletProfile.SelectedItem as TabletComboItem;
            if (item != null && item.Entry != null)
            {
                _selectedTablet = item.Entry;
                UpdateHeaderAndTabletNotes();
                UpdateSafetyBanner();
            }
        }

        private void UpdateHeaderAndTabletNotes()
        {
            if (_selectedTablet == null) return;

            string connStatus = (_hwState.IsConnected && _hwState.Tablet != null && _hwState.Tablet.id == _selectedTablet.id)
                ? ("Đã kết nối: " + _hwState.MatchedVidPid)
                : ("Hồ sơ CSDL: VID_" + _selectedTablet.vid);

            _lblDetectedSummary.Text = string.Format(
                "Thiết bị: {0} ({1}) | Hồ sơ: {2} ({3:0.00} ms quy đổi)\r\nMàn hình: {4}x{5} @ {6} Hz — Nhịp mục tiêu tham khảo: {7} Hz (1x) hoặc {8} Hz (2x)",
                _selectedTablet.name,
                connStatus,
                _selectedTablet.NativeHzDisplay,
                _selectedTablet.native_interval_ms,
                _hwState.MonitorWidth,
                _hwState.MonitorHeight,
                _hwState.MonitorRefreshHz,
                _hwState.SweetSpot1xHz,
                _hwState.SweetSpot2xHz);

            _lblRawHzSub.Text = "Nguồn: Chỉ nhận Bảng vẽ | Hồ sơ: " + _selectedTablet.NativeHzDisplay;

            _lblTabletNoteBox.Text = "• Tham khảo: " + (_selectedTablet.safe_note ?? "") + "\r\n" +
                                     "• Cảnh báo: " + (_selectedTablet.danger_note ?? "");
        }

        private void OnTargetHzOrSmoothingChanged(object sender, EventArgs e)
        {
            TargetHzOption opt = _cmbTargetHz.SelectedItem as TargetHzOption;
            if (opt != null)
            {
                _engine.TargetHz = opt.Hz;
            }

            int sm = _trkSmoothing.Value;
            _engine.SmoothingPercent = sm;

            string desc = sm == 0 ? "Tắt làm mượt" :
                          sm <= 20 ? "Hệ số thấp (cần tự thử)" :
                          sm <= 40 ? "Cân bằng — Khuyên dùng" :
                          sm <= 65 ? "Hệ số cao (cần tự thử)" : "Rất đằm (Có trễ nhẹ)";
            _lblSmoothingValue.Text = sm + "% (" + desc + ")";

            UpdateSafetyBanner();
        }

        private void UpdateSafetyBanner()
        {
            TargetHzOption opt = _cmbTargetHz.SelectedItem as TargetHzOption;
            int targetHz = opt != null ? opt.Hz : _hwState.SweetSpot2xHz;
            int sm = _trkSmoothing != null ? _trkSmoothing.Value : 35;

            SafetyEvaluation eval = HardwareDetectorAndAdvisor.EvaluateSelection(
                _selectedTablet,
                _hwState.MonitorRefreshHz,
                targetHz,
                sm);

            _lblSafetyBadgeTitle.Text = eval.BadgeTitle;
            _lblSafetyDetail.Text = eval.DetailMessage;

            if (eval.Level == SafetyLevel.OptimalGreen)
            {
                _pnlSafetyBanner.BackColor = Color.FromArgb(16, 46, 36);
                _lblSafetyBadgeTitle.ForeColor = Color.FromArgb(90, 240, 155);
                _lblSafetyDetail.ForeColor = Color.FromArgb(215, 235, 225);
                if (_mascotImg != null) _picSafetyIcon.Image = _mascotImg;
            }
            else if (eval.Level == SafetyLevel.CautionYellow)
            {
                _pnlSafetyBanner.BackColor = Color.FromArgb(52, 42, 16);
                _lblSafetyBadgeTitle.ForeColor = Color.FromArgb(255, 210, 85);
                _lblSafetyDetail.ForeColor = Color.FromArgb(245, 235, 205);
                if (_warnImg != null) _picSafetyIcon.Image = _warnImg;
            }
            else
            {
                _pnlSafetyBanner.BackColor = Color.FromArgb(62, 20, 26);
                _lblSafetyBadgeTitle.ForeColor = Color.FromArgb(255, 105, 110);
                _lblSafetyDetail.ForeColor = Color.FromArgb(252, 220, 222);
                if (_warnImg != null) _picSafetyIcon.Image = _warnImg;
            }
        }

        private void ToggleResamplerState()
        {
            bool nextState = !_engine.IsResamplerEnabled;
            _engine.SetResamplerActive(nextState);

            if (nextState)
            {
                _btnToggleResampler.Text = "ĐANG LÀM MƯỢT CON TRỎ (TẮT — F8)";
                _btnToggleResampler.BackColor = Color.FromArgb(218, 128, 28);
            }
            else
            {
                _btnToggleResampler.Text = "BẬT LÀM MƯỢT CON TRỎ (F8)";
                _btnToggleResampler.BackColor = Color.FromArgb(28, 158, 92);
            }
            OnUiTimerTick(this, EventArgs.Empty);
        }

        private void OnCleanMemoryClicked(object sender, EventArgs e)
        {
            _engine.CleanAllBuffersAndCompactRam();
            _pnlCanvas.Invalidate();
            TelemetrySnapshot snap = _engine.GetTelemetrySnapshot();
            _lblMemoryVal.Text = "Đã Dọn Sạch (0 Ô)";
            _lblMemorySub.Text = string.Format("Lịch sử mẫu cố định | Heap: {0:0.0} MB", snap.ManagedHeapBytes / (1024.0 * 1024.0));
        }

        private void LoadCurrentWacomConfigIntoUi()
        {
            WacomConfigState cfg = WacomConfigManager.ReadConfig();
            if (!cfg.Exists)
            {
                _lblWacomStatus.Text = "Trạng thái: Máy chưa cài Wacom_Tablet.dat (Chế độ dùng chung mọi hãng Tablet).";
                _lblWacomStatus.ForeColor = Color.FromArgb(240, 195, 100);
                _cmbPacketRate.SelectedIndex = 1;
                return;
            }

            if (cfg.PacketRate >= 180) _cmbPacketRate.SelectedIndex = 2;
            else if (cfg.PacketRate >= 125) _cmbPacketRate.SelectedIndex = 1;
            else _cmbPacketRate.SelectedIndex = 0;

            _chkMaxDataRate.Checked = cfg.MaxDataRateOn;
            _chkWindowsInk.Checked = cfg.WinUseInk;
            _chkFastTap.Checked = cfg.DisableDoubleClickAssist;

            _lblWacomStatus.Text = string.Format(
                "PacketRate={0} (hãng chưa công bố đơn vị) | MaxData={1} | Sao lưu: {2}",
                cfg.PacketRate,
                cfg.MaxDataRateOn ? "Bật" : "Tắt",
                cfg.HasOriginalBackup ? "Đã khóa an toàn" : "Tự tạo khi Lưu");
            _lblWacomStatus.ForeColor = Color.FromArgb(135, 218, 170);
        }

        private void OnSaveWacomConfigClicked(object sender, EventArgs e)
        {
            try
            {
                int rate = 100;
                if (_cmbPacketRate.SelectedIndex == 1) rate = 133;
                else if (_cmbPacketRate.SelectedIndex == 2) rate = 200;

                string svcMsg;
                string skippedKeys = WacomConfigManager.ApplyOptimizedConfigAndReloadService(
                    rate,
                    _chkMaxDataRate.Checked,
                    _chkWindowsInk.Checked,
                    _chkFastTap.Checked,
                    out svcMsg);

                MessageBox.Show(
                    "Đã xử lý các khóa đã có trong Wacom_Tablet.dat; khi có thay đổi, ghi bằng Atomic Swap.\nGiá trị yêu cầu (khóa thiếu được bỏ qua):\n\n" +
                    "• PacketRate: " + rate + " (giá trị trong cấu hình driver Wacom; hãng không công bố đơn vị)\n" +
                    "• MaxDataRateOn: " + (_chkMaxDataRate.Checked ? "Bật" : "Tắt") + "\n" +
                    "• Windows Ink: " + (_chkWindowsInk.Checked ? "Bật" : "Tắt") + "\n" +
                    "• DoubleClickDistance: " + (_chkFastTap.Checked ? "0.0" : "Khôi phục gốc / 2.0") + "\n\n" +
                    skippedKeys + "\n" + svcMsg,
                    "Meinya Tablet Booster — Kết Quả Cấu Hình",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                LoadCurrentWacomConfigIntoUi();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể lưu cấu hình Wacom: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnResetOriginalClicked(object sender, EventArgs e)
        {
            try
            {
                WacomConfigState cfg = WacomConfigManager.ReadConfig();
                bool restoredFromDiskBackup = false;
                string svcMsg = string.Empty;
                if (cfg.HasOriginalBackup)
                {
                    WacomConfigManager.RestoreOriginalBackupAndReloadService(out svcMsg);
                    restoredFromDiskBackup = true;
                }

                if (_engine.IsResamplerEnabled)
                {
                    ToggleResamplerState();
                }
                _trkSmoothing.Value = 35;
                _cmbTargetHz.SelectedIndex = 0;
                _engine.CleanAllBuffersAndCompactRam();
                LoadCurrentWacomConfigIntoUi();

                if (restoredFromDiskBackup)
                {
                    MessageBox.Show(
                        "Đã khôi phục Wacom_Tablet.dat từ bản .original_backup tạo ở lần sao lưu đầu, tắt làm mượt con trỏ và dọn bộ đệm.\n\n" + svcMsg,
                        "Khôi Phục Cấu Hình Ban Đầu Thành Công",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Chưa có bản .original_backup để khôi phục. Giữ nguyên Wacom_Tablet.dat hiện tại; không thể kết luận đây là cấu hình gốc.\n\n" +
                        "Đã tắt làm mượt con trỏ và dọn bộ đệm.",
                        "Khôi Phục Mặc Định Thành Công",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi khi khôi phục: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnRestartServiceAdminClicked(object sender, EventArgs e)
        {
            if (WacomConfigManager.IsRunningAsAdministrator())
            {
                string msg;
                WacomConfigManager.TryRestartWacomService(out msg);
                MessageBox.Show(msg, "Wacom Service", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = Application.ExecutablePath,
                    Verb = "runas",
                    UseShellExecute = true
                };
                Process.Start(psi);
                Application.Exit();
            }
            catch
            {
            }
        }

        private void SwitchBottomTab(bool showGlossary)
        {
            _pnlCanvas.Visible = !showGlossary;
            _txtGlossaryAndMemory.Visible = showGlossary;

            if (!showGlossary)
            {
                _btnTabCanvas.BackColor = Color.FromArgb(36, 118, 228);
                _btnTabCanvas.ForeColor = Color.White;
                _btnTabGlossary.BackColor = Color.FromArgb(34, 44, 64);
                _btnTabGlossary.ForeColor = Color.FromArgb(190, 205, 228);
            }
            else
            {
                _btnTabGlossary.BackColor = Color.FromArgb(36, 118, 228);
                _btnTabGlossary.ForeColor = Color.White;
                _btnTabCanvas.BackColor = Color.FromArgb(34, 44, 64);
                _btnTabCanvas.ForeColor = Color.FromArgb(190, 205, 228);
            }
        }

        private void PopulateGlossaryText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("=== CÔNG CỤ LÀM GÌ / KHÔNG LÀM GÌ ===");
            sb.AppendLine("Chèn bước chuột tương đối dự đoán từ vận tốc cũ; mẫu bút thật kế tiếp có thể kéo con trỏ về đầu bút.");
            sb.AppendLine("Không tăng report rate phần cứng, không thêm tọa độ/lực nhấn WinTab hoặc Windows Ink. Chỉ có thể hữu ích với ứng dụng nhận con trỏ, ví dụ osu! chế độ chuột; cần tự thử.");
            sb.AppendLine("Bước chuột chịu tốc độ con trỏ và Enhance pointer precision của Windows. Nhịp đo hook có thể gồm chuột/touchpad; không phải đo trực tiếp Hz USB của tablet.");
            sb.AppendLine();
            sb.AppendLine("=== PHẦN A: DỮ LIỆU TẠM CỦA TABLET LƯU Ở ĐÂU & CƠ CHẾ THU DỌN BỘ NHỚ 3 TẦNG ===");
            if (_db != null && _db.memory_architecture != null)
            {
                sb.AppendLine("1. " + _db.memory_architecture.tier_1_hardware_mcu);
                sb.AppendLine("2. " + _db.memory_architecture.tier_2_ram_ring_buffer);
                sb.AppendLine("3. " + _db.memory_architecture.tier_3_disk_atomic_swap);
            }
            sb.AppendLine();
            sb.AppendLine("=== PHẦN B: BẢNG CHÚ THÍCH THUẬT NGỮ & TÁC DỤNG THỰC TẾ ===");
            if (_db != null && _db.glossary != null)
            {
                for (int i = 0; i < _db.glossary.Count; i++)
                {
                    GlossaryEntry g = _db.glossary[i];
                    sb.AppendLine("• [" + g.term + "] — " + g.summary);
                    sb.AppendLine("  " + (g.detail ?? "").Replace("\n", "\r\n  "));
                    sb.AppendLine();
                }
            }
            sb.AppendLine("=== PHẦN C: HỒ SƠ THAM KHẢO (NGƯỠNG PHẦN MỀM ƯỚC LƯỢNG) ===");
            if (_db != null && _db.tablets != null)
            {
                for (int i = 0; i < _db.tablets.Count; i++)
                {
                    TabletEntry t = _db.tablets[i];
                    sb.AppendLine("[" + (i + 1) + "] " + t.name + " (Hồ sơ: " + t.NativeHzDisplay + " | Ngưỡng ước lượng: " + t.safe_software_hz_min + "-" + t.safe_software_hz_max + " Hz)");
                    sb.AppendLine("    + Đề xuất: " + t.recommended_mode);
                    sb.AppendLine("    + Tham khảo: " + t.safe_note);
                    sb.AppendLine("    + Cảnh báo: " + t.danger_note);
                    sb.AppendLine();
                }
            }
            _txtGlossaryAndMemory.Text = sb.ToString();
        }

        private void OnCanvasMouseMove(object sender, MouseEventArgs e)
        {
            long nowTick = Stopwatch.GetTimestamp();
            _engine.CanvasBuffer.Push(new CanvasSamplePoint
            {
                X = e.X,
                Y = e.Y,
                IsInterpolated = _engine.IsResamplerEnabled && _engine.WasRecentSelfInjected(nowTick),
                Tick = nowTick
            });
            _pnlCanvas.Invalidate();
        }

        private void OnCanvasPaint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (Pen gridPen = new Pen(Color.FromArgb(22, 30, 44), 1f))
            {
                int step = S(28);
                for (int x = 0; x < _pnlCanvas.Width; x += step)
                    g.DrawLine(gridPen, x, 0, x, _pnlCanvas.Height);
                for (int y = 0; y < _pnlCanvas.Height; y += step)
                    g.DrawLine(gridPen, 0, y, _pnlCanvas.Width, y);
            }

            int count = _engine.CanvasBuffer.CopyChronologicalTo(_canvasRenderScratch);
            if (count < 2)
            {
                using (Brush b = new SolidBrush(Color.FromArgb(125, 145, 175)))
                using (Font f = MakeFont(12.0f, FontStyle.Italic))
                {
                    g.DrawString(
                        "Di chuyển con trỏ để xem nhịp sự kiện chuột và đường thử (512 điểm vòng); đây không phải dữ liệu bút WinTab/Ink...",
                        f, b, new PointF(S(18), S(66)));
                }
                return;
            }

            double tickToMs = 1000.0 / Stopwatch.Frequency;
            float maxJumpPx = S((int)TemporalResamplerEngine.JumpTeleportThresholdPx);
            float maxJumpSq = maxJumpPx * maxJumpPx;

            using (Pen linePen = new Pen(_engine.IsResamplerEnabled ? Color.FromArgb(72, 198, 255) : Color.FromArgb(80, 225, 145), 2.0f))
            using (Brush rawDotBrush = new SolidBrush(Color.FromArgb(110, 245, 165)))
            using (Brush interpDotBrush = new SolidBrush(Color.FromArgb(255, 198, 82)))
            using (Brush legendBrush = new SolidBrush(Color.FromArgb(165, 185, 212)))
            using (Font legendFont = MakeFont(10.0f, FontStyle.Regular))
            {
                for (int i = 1; i < count; i++)
                {
                    CanvasSamplePoint p0 = _canvasRenderScratch[i - 1];
                    CanvasSamplePoint p1 = _canvasRenderScratch[i];
                    float dx = p1.X - p0.X;
                    float dy = p1.Y - p0.Y;
                    double dtMs = (p1.Tick >= p0.Tick) ? ((p1.Tick - p0.Tick) * tickToMs) : 9999.0;

                    // INV-3: Ngắt nét vẽ nếu khoảng nghỉ > 180ms (nhấc bút) hoặc bước nhảy > 80px
                    if (dtMs <= TemporalResamplerEngine.IdleVelocityFlushMs && (dx * dx + dy * dy) <= maxJumpSq)
                    {
                        g.DrawLine(linePen, p0.X, p0.Y, p1.X, p1.Y);
                    }
                    if (p1.IsInterpolated)
                    {
                        g.FillEllipse(interpDotBrush, p1.X - 1.9f, p1.Y - 1.9f, 3.8f, 3.8f);
                    }
                    else
                    {
                        g.FillEllipse(rawDotBrush, p1.X - 2.6f, p1.Y - 2.6f, 5.2f, 5.2f);
                    }
                }
                g.DrawString(
                    "● Xanh lá: Điểm gốc bảng vẽ   ● Vàng cam: Điểm nội suy chèn thêm",
                    legendFont,
                    legendBrush,
                    new PointF(S(10), S(6)));
            }
        }

        private void OnUiTimerTick(object sender, EventArgs e)
        {
            TelemetrySnapshot snap = _engine.GetTelemetrySnapshot();
            if (snap.RawHardwareHz > 0.5)
            {
                _lblRawHzVal.Text = Math.Round(snap.RawHardwareHz) + " Hz";
                _lblOutHzVal.Text = Math.Round(snap.EffectiveOutputHz) + " Hz";
                _lblIntervalVal.Text = snap.AvgIntervalMs.ToString("0.00") + " ms";
            }
            else
            {
                _lblRawHzVal.Text = "0 Hz (Chờ bút)";
                _lblOutHzVal.Text = _engine.IsResamplerEnabled ? ("Mục tiêu " + _engine.TargetHz + " Hz") : "0 Hz (Chờ bút)";
                _lblIntervalVal.Text = "-- ms";
            }

            string profileHz = _selectedTablet != null ? (_selectedTablet.native_hz + "Hz") : "133Hz";
            _lblRawHzSub.Text = string.Format(
                "{0} | Lọc chuột: {1} ({2})",
                snap.ActiveInputSourceLabel,
                snap.IgnoredMouseEventsCount,
                profileHz);
            _lblOutHzSub.Text = _engine.IsResamplerEnabled
                ? ("Đang làm mượt (Mục tiêu " + _engine.TargetHz + " Hz)")
                : "Đang tắt làm mượt (Bấm nút hoặc F8)";
            _lblIntervalSub.Text = string.Format(
                "Đỉnh: {0} Hz | Jitter: ±{1:0.00} ms",
                Math.Round(snap.PeakRawHz),
                snap.JitterMs);
            _lblMemoryVal.Text = string.Format("Đệm: {0}/{1} ô", snap.RingBufferActiveItems, snap.RingBufferMaxCapacity);
            _lblMemorySub.Text = string.Format(
                "Xoay vòng: {0} | Heap: {1:0.0} MB | {2}",
                snap.RingBufferRecycledCount,
                snap.ManagedHeapBytes / (1024.0 * 1024.0),
                snap.VelocityBufferFlushed ? "Đã xả vận tốc" : "Đang nội suy");
        }

        public bool CheckNoOverlappingControls(out string report)
        {
            List<string> overlaps = new List<string>();
            CheckContainerOverlaps(this, overlaps);
            if (overlaps.Count == 0)
            {
                report = "0 overlapping controls";
                return true;
            }
            report = string.Join("; ", overlaps.ToArray());
            return false;
        }

        private static void CheckContainerOverlaps(Control parent, List<string> overlaps)
        {
            for (int i = 0; i < parent.Controls.Count; i++)
            {
                Control a = parent.Controls[i];
                if (!a.Visible) continue;
                for (int j = i + 1; j < parent.Controls.Count; j++)
                {
                    Control b = parent.Controls[j];
                    if (!b.Visible) continue;
                    Rectangle inter = Rectangle.Intersect(a.Bounds, b.Bounds);
                    if (inter.Width > 2 && inter.Height > 2)
                    {
                        overlaps.Add(parent.Name + ":[" + a.GetType().Name + " '" + a.Text + "' overlaps " + b.GetType().Name + " '" + b.Text + "']");
                    }
                }
                if (a.HasChildren)
                {
                    CheckContainerOverlaps(a, overlaps);
                }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_uiTimer != null) _uiTimer.Stop();
            _engine.Dispose();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_uiTimer != null)
                {
                    _uiTimer.Stop();
                    _uiTimer.Dispose();
                    _uiTimer = null;
                }
                if (_engine != null)
                {
                    _engine.Dispose();
                }
                if (_mascotImg != null)
                {
                    _mascotImg.Dispose();
                    _mascotImg = null;
                }
                if (_warnImg != null)
                {
                    _warnImg.Dispose();
                    _warnImg = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
