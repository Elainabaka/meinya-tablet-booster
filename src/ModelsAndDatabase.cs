using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace MeinyaTabletBooster
{
    public enum SafetyLevel
    {
        OptimalGreen = 0,
        CautionYellow = 1,
        DangerRed = 2
    }

    public sealed class TabletEntry
    {
        public string id { get; set; }
        public string brand { get; set; }
        public string name { get; set; }
        public string vid { get; set; }
        public List<string> pids { get; set; }
        public int native_hz { get; set; }
        public string hz_nguon { get; set; }
        public string NativeHzDisplay
        {
            get
            {
                return native_hz + " Hz (" +
                    (string.IsNullOrEmpty(hz_nguon) || hz_nguon == "uoc-luong" ? "ước lượng" : "nguồn: " + hz_nguon) + ")";
            }
        }
        public double native_interval_ms { get; set; }
        public int safe_software_hz_min { get; set; }
        public int safe_software_hz_max { get; set; }
        public string recommended_mode { get; set; }
        public string safe_note { get; set; }
        public string danger_note { get; set; }

        public TabletEntry()
        {
            pids = new List<string>();
        }
    }

    public sealed class GlossaryEntry
    {
        public string term { get; set; }
        public string summary { get; set; }
        public string detail { get; set; }
    }

    public sealed class MemoryArchitectureInfo
    {
        public string tier_1_hardware_mcu { get; set; }
        public string tier_2_ram_ring_buffer { get; set; }
        public string tier_3_disk_atomic_swap { get; set; }
    }

    public sealed class SweetSpotRulesInfo
    {
        public string sync_1x_description { get; set; }
        public string double_2x_description { get; set; }
        public int safe_software_ceiling_hz { get; set; }
        public int warning_overkill_hz { get; set; }
        public int danger_extreme_hz { get; set; }
    }

    public sealed class TabletDatabaseRoot
    {
        public string schema_version { get; set; }
        public string project { get; set; }
        public string author { get; set; }
        public string description { get; set; }
        public SweetSpotRulesInfo sweet_spot_rules { get; set; }
        public MemoryArchitectureInfo memory_architecture { get; set; }
        public List<TabletEntry> tablets { get; set; }
        public List<GlossaryEntry> glossary { get; set; }

        public TabletDatabaseRoot()
        {
            tablets = new List<TabletEntry>();
            glossary = new List<GlossaryEntry>();
            sweet_spot_rules = new SweetSpotRulesInfo
            {
                safe_software_ceiling_hz = 360,
                warning_overkill_hz = 400,
                danger_extreme_hz = 500
            };
            memory_architecture = new MemoryArchitectureInfo();
        }
    }

    public sealed class DetectedHardwareState
    {
        public bool IsConnected { get; set; }
        public string MatchedVidPid { get; set; }
        public TabletEntry Tablet { get; set; }
        public int MonitorWidth { get; set; }
        public int MonitorHeight { get; set; }
        public int MonitorRefreshHz { get; set; }
        public int SweetSpot1xHz { get; set; }
        public int SweetSpot2xHz { get; set; }
    }

    public sealed class SafetyEvaluation
    {
        public SafetyLevel Level { get; set; }
        public string BadgeTitle { get; set; }
        public string DetailMessage { get; set; }
    }

    public static class TabletDatabaseManager
    {
        public static string ResolveDatabasePath()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string candidate1 = Path.Combine(baseDir, "data", "tablets_db.json");
            if (File.Exists(candidate1)) return candidate1;

            string candidate2 = Path.Combine(baseDir, "..", "data", "tablets_db.json");
            if (File.Exists(candidate2)) return Path.GetFullPath(candidate2);

            return candidate1;
        }

        public static TabletDatabaseRoot LoadDatabase()
        {
            // Thứ tự: data/tablets_db.json cạnh exe (người dùng tự sửa được), rồi bản nhúng trong exe
            // (tải mỗi exe từ Releases vẫn đủ hồ sơ), cuối cùng bộ dự phòng một hồ sơ.
            string path = ResolveDatabasePath();
            try
            {
                if (File.Exists(path))
                {
                    TabletDatabaseRoot root = ParseDatabase(File.ReadAllText(path, Encoding.UTF8));
                    if (root != null) return root;
                }
            }
            catch
            {
                // JSON hỏng: thử bản nhúng
            }
            try
            {
                using (Stream s = typeof(TabletDatabaseManager).Assembly.GetManifestResourceStream("tablets_db.json"))
                {
                    if (s != null)
                    {
                        using (StreamReader r = new StreamReader(s, Encoding.UTF8))
                        {
                            TabletDatabaseRoot root = ParseDatabase(r.ReadToEnd());
                            if (root != null) return root;
                        }
                    }
                }
            }
            catch
            {
                // bản nhúng hỏng: dùng bộ dự phòng
            }
            return CreateFallbackDatabase();
        }

        private static TabletDatabaseRoot ParseDatabase(string json)
        {
            JavaScriptSerializer serializer = new JavaScriptSerializer();
            serializer.MaxJsonLength = 4 * 1024 * 1024;
            TabletDatabaseRoot root = serializer.Deserialize<TabletDatabaseRoot>(json);
            return root != null && root.tablets != null && root.tablets.Count > 0 ? root : null;
        }

        public static TabletDatabaseRoot CreateFallbackDatabase()
        {
            TabletDatabaseRoot root = new TabletDatabaseRoot
            {
                schema_version = "1.0.0",
                project = "Meinya Tablet Booster",
                author = "Elainabaka"
            };
            root.memory_architecture = new MemoryArchitectureInfo
            {
                tier_1_hardware_mcu = "Công cụ không đọc bộ nhớ MCU hay sửa firmware; chưa có nguồn xác nhận kích thước FIFO.",
                tier_2_ram_ring_buffer = "Hai bộ đệm khoảng thời gian 256 ô và canvas 512 điểm có dung lượng cố định. Worker xả vận tốc sau 180 ms không có chuyển động.",
                tier_3_disk_atomic_swap = "Trên ổ cứng (Atomic File Swap): Khóa bất biến bản gốc .original_backup, ghi qua file tạm .tmp rồi tráo nguyên tử vào Wacom_Tablet.dat."
            };
            root.tablets.Add(new TabletEntry
            {
                id = "wacom-ctl-472",
                brand = "Wacom",
                name = "One by Wacom Small (CTL-472)",
                vid = "056A",
                pids = new List<string> { "037A" },
                native_hz = 133,
                hz_nguon = "uoc-luong",
                native_interval_ms = 7.52,
                safe_software_hz_min = 133,
                safe_software_hz_max = 360,
                recommended_mode = "1x hoặc 2x tần số quét màn hình",
                safe_note = "Nhịp mục tiêu 1x/2x màn hình là mức thử tham khảo. Số Hz và các ngưỡng là ước lượng; không tăng tần số báo cáo phần cứng.",
                danger_note = "Chỉ chèn chuyển động chuột tương đối; không thêm dữ liệu bút WinTab/Ink hay lực nhấn. PacketRate chưa có đơn vị được hãng công bố."
            });
            root.glossary.Add(new GlossaryEntry
            {
                term = "Điểm ngọt 1x & 2x Màn hình",
                summary = "Nhịp mục tiêu tham khảo để thử độ mượt con trỏ.",
                detail = "1x/2x không đồng bộ với thời điểm quét màn hình. 330 Hz có chu kỳ khoảng 3,03 ms nếu sự kiện đều; không bảo đảm độ trễ. WinTab/Ink không đi qua bộ làm mượt chuột."
            });
            return root;
        }
    }

    public static class HardwareDetectorAndAdvisor
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DEVMODEA
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] dmDeviceName;
            public short dmSpecVersion;
            public short dmDriverVersion;
            public short dmSize;
            public short dmDriverExtra;
            public int dmFields;
            public int dmPositionX;
            public int dmPositionY;
            public int dmDisplayOrientation;
            public int dmDisplayFixedOutput;
            public short dmColor;
            public short dmDuplex;
            public short dmYResolution;
            public short dmTTOption;
            public short dmCollate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel;
            public int dmPelsWidth;
            public int dmPelsHeight;
            public int dmDisplayFlags;
            public int dmDisplayFrequency;
            public int dmICMMethod;
            public int dmICMIntent;
            public int dmMediaType;
            public int dmDitherType;
            public int dmReserved1;
            public int dmReserved2;
            public int dmPanningWidth;
            public int dmPanningHeight;
        }

        [DllImport("user32.dll", CharSet = CharSet.Ansi)]
        private static extern bool EnumDisplaySettingsA(string lpszDeviceName, int iModeNum, ref DEVMODEA lpDevMode);

        private const int ENUM_CURRENT_SETTINGS = -1;

        public static void GetPrimaryMonitorSpecs(out int width, out int height, out int refreshHz)
        {
            width = 1920;
            height = 1080;
            refreshHz = 60;
            try
            {
                DEVMODEA dm = new DEVMODEA();
                dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODEA));
                if (EnumDisplaySettingsA(null, ENUM_CURRENT_SETTINGS, ref dm))
                {
                    if (dm.dmPelsWidth > 0) width = dm.dmPelsWidth;
                    if (dm.dmPelsHeight > 0) height = dm.dmPelsHeight;
                    if (dm.dmDisplayFrequency > 1) refreshHz = dm.dmDisplayFrequency;
                }
            }
            catch
            {
            }
        }

        public static DetectedHardwareState DetectConnectedTablet(TabletDatabaseRoot db)
        {
            int monW, monH, monHz;
            GetPrimaryMonitorSpecs(out monW, out monH, out monHz);

            int sweet1x = monHz;
            int sweet2x = monHz * 2;

            DetectedHardwareState state = new DetectedHardwareState
            {
                MonitorWidth = monW,
                MonitorHeight = monH,
                MonitorRefreshHz = monHz,
                SweetSpot1xHz = sweet1x,
                SweetSpot2xHz = sweet2x,
                IsConnected = false,
                MatchedVidPid = "Chưa cắm USB hoặc đang dùng chế độ mô phỏng"
            };

            string pnpOutput = RunPnpUtilConnectedHid();
            if (!string.IsNullOrEmpty(pnpOutput) && db != null && db.tablets != null)
            {
                foreach (TabletEntry entry in db.tablets)
                {
                    if (string.IsNullOrEmpty(entry.vid) || entry.pids == null) continue;
                    foreach (string pid in entry.pids)
                    {
                        string pattern = "VID_" + entry.vid.ToUpperInvariant() + "&PID_" + pid.ToUpperInvariant();
                        if (pnpOutput.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            state.IsConnected = true;
                            state.MatchedVidPid = pattern;
                            state.Tablet = entry;
                            return state;
                        }
                    }
                }
            }

            // Fallback: select the first tablet in database (e.g., CTL-472) as active profile
            if (db != null && db.tablets != null && db.tablets.Count > 0)
            {
                state.Tablet = db.tablets[0];
            }
            else
            {
                state.Tablet = TabletDatabaseManager.CreateFallbackDatabase().tablets[0];
            }
            return state;
        }

        private static string RunPnpUtilConnectedHid()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "pnputil.exe",
                    Arguments = "/enum-devices /connected /class HIDClass",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return string.Empty;
                    string output = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(2500);
                    return output ?? string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static SafetyEvaluation EvaluateSelection(TabletEntry tablet, int monitorHz, int targetHz, int interpolationPercent)
        {
            string nativeHz = tablet != null ? tablet.NativeHzDisplay : "133 Hz (ước lượng)";
            int safeMax = (tablet != null && tablet.safe_software_hz_max > 0) ? tablet.safe_software_hz_max : 360;
            int sweet1x = monitorHz > 0 ? monitorHz : 165;
            int sweet2x = sweet1x * 2;
            string tabletName = (tablet != null && !string.IsNullOrEmpty(tablet.name)) ? tablet.name : "Bảng vẽ hiện tại";

            // Case 1: Extreme / Exceeding stability envelope (RED WARNING)
            if (targetHz >= 480 || targetHz > safeMax + 60)
            {
                return new SafetyEvaluation
                {
                    Level = SafetyLevel.DangerRed,
                    BadgeTitle = "CẢNH BÁO NHỊP MỤC TIÊU CAO (" + targetHz + " Hz)",
                    DetailMessage = "Ngưỡng phần mềm " + safeMax + " Hz là ước lượng cho " + tabletName +
                                    "; màu cảnh báo không phải giới hạn phần cứng. Nhịp cao có thể tốn CPU hoặc gây rung con trỏ. " +
                                    "Thử " + sweet1x + " Hz (1x) hoặc " + sweet2x + " Hz (2x), rồi đánh giá trong ứng dụng."
                };
            }

            // Case 2: Exceeding profile safe ceiling OR exceeding 2x monitor sweet spot (YELLOW CAUTION)
            if (targetHz > safeMax || targetHz > sweet2x + 10)
            {
                return new SafetyEvaluation
                {
                    Level = SafetyLevel.CautionYellow,
                    BadgeTitle = "LƯU Ý: VƯỢT NGƯỠNG THAM KHẢO (" + targetHz + " Hz)",
                    DetailMessage = "Mức này vượt ngưỡng phần mềm " + safeMax + " Hz (ước lượng) hoặc cao hơn 2x màn hình " + sweet1x +
                                    " Hz. Đây là nhịp mục tiêu chèn chuột, không phải Hz báo cáo của " + tabletName + ". Hiệu quả cần tự thử."
                };
            }

            if (interpolationPercent > 65)
            {
                return new SafetyEvaluation
                {
                    Level = SafetyLevel.CautionYellow,
                    BadgeTitle = "LƯU Ý: ĐỘ BÁM QUỸ ĐẠO CAO (" + interpolationPercent + "%)",
                    DetailMessage = "Hệ số " + interpolationPercent + "% khá cao; bước chuột dự đoán có thể làm con trỏ lệch đầu bút. " +
                                    "Thử giảm hệ số nếu thấy trễ hoặc rung. Bộ này không xử lý nét bút WinTab/Ink."
                };
            }

            // Case 3: Sweet Spot 1x or 2x or Safe Range (GREEN OPTIMAL)
            bool isExact1x = Math.Abs(targetHz - sweet1x) <= 5;
            bool isExact2x = Math.Abs(targetHz - sweet2x) <= 10;

            if (isExact2x)
            {
                return new SafetyEvaluation
                {
                    Level = SafetyLevel.OptimalGreen,
                    BadgeTitle = "NHỊP MỤC TIÊU 2X MÀN HÌNH (" + targetHz + " Hz — THAM KHẢO)",
                    DetailMessage = "2x là mức thử, không khóa pha với màn hình hay bảo đảm độ trễ. Hồ sơ thiết bị: " + nativeHz +
                                    ". Bộ làm mượt chỉ chèn chuyển động chuột, không tăng Hz phần cứng hoặc thêm dữ liệu bút WinTab/Ink."
                };
            }

            if (isExact1x)
            {
                return new SafetyEvaluation
                {
                    Level = SafetyLevel.OptimalGreen,
                    BadgeTitle = "NHỊP MỤC TIÊU 1X MÀN HÌNH (" + targetHz + " Hz — THAM KHẢO)",
                    DetailMessage = "1x lấy tần số màn hình làm nhịp mục tiêu, không đồng bộ thời điểm quét khung hình. Hồ sơ: " + nativeHz +
                                    ". Gói thật vẫn đi qua nên tổng đầu ra không được bảo đảm bằng nhịp mục tiêu."
                };
            }

            return new SafetyEvaluation
            {
                Level = SafetyLevel.OptimalGreen,
                BadgeTitle = "TRONG NGƯỠNG THAM KHẢO (" + targetHz + " Hz)",
                DetailMessage = "Ngưỡng phần mềm " + safeMax + " Hz là ước lượng; không chứng minh an toàn hay độ mượt. Hồ sơ thiết bị: " + nativeHz +
                                ". Có thể thử " + sweet1x + " Hz (1x) hoặc " + sweet2x + " Hz (2x) và tự đánh giá."
            };
        }
    }
}

