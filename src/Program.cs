using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Meinya Tablet Booster")]
[assembly: AssemblyDescription("Đo nhịp sự kiện chuột, làm mượt con trỏ & quản lý cấu hình Wacom — by Elainabaka")]
[assembly: AssemblyCompany("Elainabaka")]
[assembly: AssemblyProduct("Meinya Tablet Booster")]
[assembly: AssemblyCopyright("Copyright © 2026 Elainabaka")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace MeinyaTabletBooster
{
    public static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                if (string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
                {
                    int code = RunSelfTest();
                    Environment.Exit(code);
                    return code;
                }
                if (string.Equals(args[0], "--ui-smoke-test", StringComparison.OrdinalIgnoreCase))
                {
                    try { SetProcessDPIAware(); } catch { }
                    int code = RunUiSmokeTest();
                    Environment.Exit(code);
                    return code;
                }
            }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static int RunSelfTest()
        {
            try
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }

                // 1. Kiểm tra nạp CSDL data/tablets_db.json
                TabletDatabaseRoot db = TabletDatabaseManager.LoadDatabase();
                if (db == null || db.tablets == null || db.tablets.Count < 10)
                {
                    Console.Error.WriteLine("SELF-TEST FAIL: tablets_db.json failed to load enough tablet profiles.");
                    return 1;
                }
                if (db.glossary == null || db.glossary.Count < 5)
                {
                    Console.Error.WriteLine("SELF-TEST FAIL: glossary entries missing in tablets_db.json.");
                    return 1;
                }

                // Q380: Kiểm tra dữ liệu, không quét thiết bị thật hay mở giao diện.
                foreach (TabletEntry tablet in db.tablets)
                {
                    if (tablet.hz_nguon != "uoc-luong" || !tablet.NativeHzDisplay.Contains("ước lượng"))
                    {
                        throw new InvalidOperationException("SELF-TEST FAIL: Missing estimate label for " + tablet.id);
                    }
                }
                if (string.Join(",", db.tablets.Find(t => t.id == "wacom-ctl-680").pids) != "0323" ||
                    string.Join(",", db.tablets.Find(t => t.id == "wacom-cth-680").pids) != "0303" ||
                    string.Join(",", db.tablets.Find(t => t.id == "wacom-ctl-480").pids) != "030E,0302" ||
                    string.Join(",", db.tablets.Find(t => t.id == "wacom-ptk-670").pids) != "03F5,03F7,03F9" ||
                    string.Join(",", db.tablets.Find(t => t.id == "xencelabs-tablet").pids) != "5204,5201" ||
                    !TabletDatabaseManager.CreateFallbackDatabase().tablets[0].NativeHzDisplay.Contains("ước lượng"))
                {
                    throw new InvalidOperationException("SELF-TEST FAIL: Q380 PID or fallback label mismatch.");
                }

                // 2. Cảnh báo 3 mức trên dữ liệu giả; không đo màn hình hay phần cứng thật.
                TabletEntry ctl472 = db.tablets[0];
                SafetyEvaluation evalGreen = HardwareDetectorAndAdvisor.EvaluateSelection(ctl472, 165, 330, 35);
                SafetyEvaluation evalYellow = HardwareDetectorAndAdvisor.EvaluateSelection(ctl472, 165, 420, 35);
                SafetyEvaluation evalYellow240 = HardwareDetectorAndAdvisor.EvaluateSelection(ctl472, 240, 420, 35);
                SafetyEvaluation evalRed = HardwareDetectorAndAdvisor.EvaluateSelection(ctl472, 165, 500, 35);

                if (evalGreen.Level != SafetyLevel.OptimalGreen ||
                    evalYellow.Level != SafetyLevel.CautionYellow ||
                    evalYellow240.Level != SafetyLevel.CautionYellow ||
                    evalRed.Level != SafetyLevel.DangerRed)
                {
                    Console.Error.WriteLine("SELF-TEST FAIL: SweetSpotAdvisor safety levels mismatch.");
                    return 1;
                }

                // 3. Kiểm tra INV-2: FixedRingBuffer chạy 100.000 vòng lặp không vượt trần dung lượng
                FixedRingBuffer<double> ring = new FixedRingBuffer<double>(256);
                for (int i = 0; i < 100000; i++)
                {
                    ring.Push(i * 0.5);
                }
                if (ring.Count != 256 || ring.TotalOverwrittenCount != (100000 - 256))
                {
                    Console.Error.WriteLine("SELF-TEST FAIL: FixedRingBuffer capacity or overwrite count invariant violated.");
                    return 1;
                }
                double[] copyArr = new double[256];
                int copied = ring.CopyChronologicalTo(copyArr);
                if (copied != 256 || Math.Abs(copyArr[255] - (99999 * 0.5)) > 1e-6)
                {
                    Console.Error.WriteLine("SELF-TEST FAIL: FixedRingBuffer chronological order check failed.");
                    return 1;
                }

                // 4. Kiểm tra INV-1, INV-3: TemporalResamplerEngine đo 133Hz, phân biệt Chuột rời vs Bảng vẽ, chống văng lố & tự xả đệm
                using (TemporalResamplerEngine engine = new TemporalResamplerEngine())
                {
                    long freq = Stopwatch.Frequency;
                    long stepTicks = (long)Math.Round(freq * (7.51879699 / 1000.0)); // ~133 Hz
                    long baseTick = Stopwatch.GetTimestamp() - (40 * stepTicks);
                    for (int i = 0; i < 40; i++)
                    {
                        engine.ProcessRawHardwareSample(100 + i * 3, 200 + i * 2, baseTick + i * stepTicks);
                    }
                    TelemetrySnapshot snap = engine.GetTelemetrySnapshot();
                    if (snap.RawHardwareHz < 128.0 || snap.RawHardwareHz > 138.0)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Simulated 133Hz measured as " + snap.RawHardwareHz);
                        return 1;
                    }
                    if (snap.JitterMs > 0.25)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Uniform 133Hz simulated jitter too high: " + snap.JitterMs);
                        return 1;
                    }
                    if (engine.RemainingStepBudgetPx <= 0.0 || engine.RemainingStepBudgetPx > 12.0)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Anti-overshoot step budget out of range: " + engine.RemainingStepBudgetPx);
                        return 1;
                    }

                    // Kiểm tra Quantized 1€ Filter khi phanh gấp về bước 1 px
                    engine.ProcessRawHardwareSample(100 + 39 * 3 + 1, 200 + 39 * 2, baseTick + 40 * stepTicks);
                    if (engine.RemainingStepBudgetPx >= 1.0)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Fine 1px deceleration budget exceeded 1.0px: " + engine.RemainingStepBudgetPx);
                        return 1;
                    }

                    // Kiểm tra lọc phân biệt Tự bơm (INV-1) vs Windows Ink Pen vs Wacom/Driver Inject vs Chuột rời vs HID fallback
                    long testNow = Stopwatch.GetTimestamp();
                    long future1Sec = testNow + freq;
                    long future4Sec = testNow + (4L * freq);
                    bool selfInj;
                    if (engine.EvaluateHookMouseMove(TemporalResamplerEngine.InjectedSignature, 0, testNow, out selfInj) || !selfInj)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: InjectedSignature was not blocked by INV-1.");
                        return 1;
                    }
                    if (!engine.WasRecentSelfInjected(testNow))
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: WasRecentSelfInjected did not return true after InjectedSignature.");
                        return 1;
                    }
                    if (!engine.EvaluateHookMouseMove(0xFF515701L, 0, testNow, out selfInj) || selfInj)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Windows Ink pen signature (0xFF515701) was wrongly rejected.");
                        return 1;
                    }
                    if (!engine.EvaluateHookMouseMove(0xFF515781L, 1, testNow, out selfInj) || selfInj)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Windows 10/11 Ink pen signature (0xFF515781, flags=1) was wrongly rejected.");
                        return 1;
                    }
                    if (!engine.EvaluateHookMouseMove(0, 1, testNow, out selfInj) || selfInj)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: User-mode tablet driver injected packet (flags=1) was wrongly rejected.");
                        return 1;
                    }
                    if (engine.EvaluateHookMouseMove(0, 0, future1Sec, out selfInj) || selfInj)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Physical mouse event 1s after tablet input was wrongly accepted.");
                        return 1;
                    }
                    if (!engine.GetTelemetrySnapshot().VelocityBufferFlushed || engine.RemainingStepBudgetPx != 0.0)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Physical mouse rejection did not flush pending velocity/budget.");
                        return 1;
                    }
                    if (!engine.EvaluateHookMouseMove(0, 0, future4Sec, out selfInj) || selfInj)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: HID fallback after 4s idle was wrongly rejected.");
                        return 1;
                    }

                    // Kiểm tra hồi quy lỗi 170ms gap + nhảy > 80px phải xóa sạch vận tốc (INV-3)
                    long gap170MsTicks = (long)Math.Round(freq * 0.170);
                    engine.ProcessRawHardwareSample(400, 500, baseTick + 40 * stepTicks + gap170MsTicks);
                    TelemetrySnapshot afterJump = engine.GetTelemetrySnapshot();
                    if (!afterJump.VelocityBufferFlushed)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: 170ms idle gap + jump did not flush velocity buffer.");
                        return 1;
                    }

                    engine.CleanAllBuffersAndCompactRam();
                    TelemetrySnapshot afterClean = engine.GetTelemetrySnapshot();
                    if (afterClean.RingBufferActiveItems != 0 || !afterClean.VelocityBufferFlushed)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: CleanAllBuffersAndCompactRam did not flush buffers.");
                        return 1;
                    }
                }

                // Kiểm tra phân biệt trùng VID/PID giữa Huion và Gaomon
                string vidPidOut;
                bool connOut;
                TabletEntry gaomonMatch = HardwareDetectorAndAdvisor.MatchTabletFromPnpAndPointer(
                    db,
                    "Instance ID: HID\\VID_256C&PID_006D\\1\r\nDevice Description: Gaomon S620 Pen Tablet",
                    null,
                    out vidPidOut,
                    out connOut);
                if (!connOut || gaomonMatch == null || gaomonMatch.id != "gaomon-s620")
                {
                    Console.Error.WriteLine("SELF-TEST FAIL: Shared VID/PID disambiguation failed for Gaomon S620.");
                    return 1;
                }
                TabletEntry huionMatch = HardwareDetectorAndAdvisor.MatchTabletFromPnpAndPointer(
                    db,
                    "Instance ID: HID\\VID_256C&PID_006D\\1\r\nDevice Description: Huion H640P Pen Tablet",
                    null,
                    out vidPidOut,
                    out connOut);
                if (!connOut || huionMatch == null || huionMatch.id != "huion-inspiroy")
                {
                    Console.Error.WriteLine("SELF-TEST FAIL: Shared VID/PID disambiguation failed for Huion Inspiroy.");
                    return 1;
                }

                // 5. Kiểm tra INV-4 & INV-5: Atomic File Swap & Immutable Original Backup
                string tempDir = Path.Combine(Path.GetTempPath(), "MeinyaBoosterTest_" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    string mockDat = Path.Combine(tempDir, "Wacom_Tablet.dat");
                    string initialContent =
                        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<root>\r\n" +
                        "\t<PacketRate type=\"integer\">100</PacketRate>\r\n" +
                        "\t<ThinLine type=\"bool\">true</ThinLine>\r\n" +
                        "\t<MaxDataRateOn type=\"bool\">false</MaxDataRateOn>\r\n" +
                        "\t<SendBatchedData type=\"bool\">true</SendBatchedData>\r\n" +
                        "\t<WinUseInk type=\"bool\">true</WinUseInk>\r\n" +
                        "\t<DoubleClickDistance type=\"double\">2.000000</DoubleClickDistance>\r\n" +
                        "</root>\r\n";
                    byte[] initialBytes = new UTF8Encoding(false).GetBytes(initialContent);
                    File.WriteAllBytes(mockDat, initialBytes);

                    // Lần lưu 1: PacketRate=200, MaxDataRateOn=true, WinUseInk=false, FastTap=true
                    WacomConfigManager.ApplyOptimizedConfig(200, true, false, true, mockDat);
                    WacomConfigState s1 = WacomConfigManager.ReadConfig(mockDat);
                    if (s1.PacketRate != 200 || !s1.MaxDataRateOn || s1.WinUseInk || !s1.DisableDoubleClickAssist || !s1.HasOriginalBackup)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: First ApplyOptimizedConfig state mismatch.");
                        return 1;
                    }

                    // Lần lưu 2: Đổi sang PacketRate=133 -> .original_backup tuyệt đối KHÔNG được bị ghi đè bởi lần 1
                    WacomConfigManager.ApplyOptimizedConfig(133, false, true, false, mockDat);
                    string[] leftoverTmp = Directory.GetFiles(tempDir, "*.tmp*");
                    if (leftoverTmp.Length != 0)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Leftover .tmp files found.");
                        return 1;
                    }

                    // Khôi phục bản gốc -> phải giống hệt từng byte lúc đầu (PacketRate=100, MaxDataRateOn=false)
                    WacomConfigManager.RestoreOriginalBackup(mockDat);
                    byte[] restoredBytes = File.ReadAllBytes(mockDat);
                    if (restoredBytes.Length != initialBytes.Length)
                    {
                        Console.Error.WriteLine("SELF-TEST FAIL: Restored byte length mismatch.");
                        return 1;
                    }
                    for (int i = 0; i < initialBytes.Length; i++)
                    {
                        if (restoredBytes[i] != initialBytes[i])
                        {
                            Console.Error.WriteLine("SELF-TEST FAIL: Restored byte content mismatch at index " + i);
                            return 1;
                        }
                    }

                    // Q380: Thiếu khóa, kể cả có các thẻ từng được dùng làm điểm chèn.
                    // Thử UTF-8 có/không BOM, LF/CRLF; cả file lẫn backup phải nguyên byte.
                    for (int variant = 0; variant < 4; variant++)
                    {
                        string missingDat = Path.Combine(tempDir, "missing-" + variant + ".dat");
                        string eol = variant < 2 ? "\n" : "\r\n";
                        string missingContent = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + eol +
                            "<root><!-- Dữ liệu giả Q380 -->" + eol +
                            "<ThinLine type=\"bool\">true</ThinLine>" + eol +
                            "<SendBatchedData type=\"bool\">true</SendBatchedData>" + eol + "</root>" + eol;
                        File.WriteAllText(missingDat, missingContent, new UTF8Encoding(variant % 2 == 1));
                        byte[] before = File.ReadAllBytes(missingDat);
                        for (int pass = 0; pass < 2; pass++)
                        {
                            string warning = WacomConfigManager.ApplyOptimizedConfig(200, true, false, true, missingDat);
                            foreach (string key in new[] { "PacketRate", "MaxDataRateOn", "WinUseInk", "DoubleClickDistance" })
                            {
                                if (!warning.Contains("driver này không có khóa " + key + ", không sửa"))
                                    throw new InvalidOperationException("SELF-TEST FAIL: Missing warning for " + key);
                            }
                            if (Convert.ToBase64String(before) != Convert.ToBase64String(File.ReadAllBytes(missingDat)) ||
                                Convert.ToBase64String(before) != Convert.ToBase64String(File.ReadAllBytes(WacomConfigManager.GetOriginalBackupPath(missingDat))))
                                throw new InvalidOperationException("SELF-TEST FAIL: Missing-key file/backup byte content changed.");
                        }
                    }

                    // Thiếu riêng từng khóa: khóa còn lại vẫn phải được sửa, không chèn khóa thiếu.
                    for (int variant = 0; variant < 2; variant++)
                    {
                        string partialDat = Path.Combine(tempDir, "partial-" + variant + ".dat");
                        string missingKey = variant == 0 ? "PacketRate" : "MaxDataRateOn";
                        string partialContent = "<root>\n<ThinLine type=\"bool\">true</ThinLine>\n" +
                            "<SendBatchedData type=\"bool\">true</SendBatchedData>\n" +
                            (variant == 0 ? "<MaxDataRateOn type=\"bool\">false</MaxDataRateOn>\n" : "<PacketRate type=\"integer\">100</PacketRate>\n") +
                            "<WinUseInk type=\"bool\">true</WinUseInk>\n" +
                            "<DoubleClickDistance type=\"double\">2.000000</DoubleClickDistance>\n</root>\n";
                        File.WriteAllText(partialDat, partialContent, new UTF8Encoding(false));
                        string warning = WacomConfigManager.ApplyOptimizedConfig(200, true, false, true, partialDat);
                        string expected = partialContent.Replace(">100</PacketRate>", ">200</PacketRate>")
                            .Replace(">false</MaxDataRateOn>", ">true</MaxDataRateOn>")
                            .Replace(">true</WinUseInk>", ">false</WinUseInk>")
                            .Replace(">2.000000</DoubleClickDistance>", ">0.000000</DoubleClickDistance>");
                        if (File.ReadAllText(partialDat, Encoding.UTF8) != expected ||
                            !warning.Contains("driver này không có khóa " + missingKey + ", không sửa"))
                            throw new InvalidOperationException("SELF-TEST FAIL: Partial-key update mismatch.");
                    }
                    if (Directory.GetFiles(tempDir, "*.tmp*").Length != 0)
                        throw new InvalidOperationException("SELF-TEST FAIL: Q380 leftover .tmp files found.");

                    // Kiểm tra khôi phục DoubleClickDistance gốc (ví dụ 3.500000) khi bỏ chọn FastTap
                    string customDblDat = Path.Combine(tempDir, "custom-dbl.dat");
                    string customDblContent = "<root>\n<DoubleClickDistance type=\"double\">3.500000</DoubleClickDistance>\n</root>\n";
                    File.WriteAllText(customDblDat, customDblContent, new UTF8Encoding(false));
                    WacomConfigManager.ApplyOptimizedConfig(133, true, true, true, customDblDat);
                    WacomConfigManager.ApplyOptimizedConfig(133, true, true, false, customDblDat);
                    if (File.ReadAllText(customDblDat, Encoding.UTF8) != customDblContent)
                        throw new InvalidOperationException("SELF-TEST FAIL: DoubleClickDistance original value was not restored.");
                }
                finally
                {
                    try
                    {
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, true);
                        }
                    }
                    catch
                    {
                    }
                }

                Console.WriteLine(string.Format(
                    "SELF-TEST OK | Profiles={0} | Glossary={1} | Q380Data=PASS | AdvisoryCases=PASS | SimulatedRaw133Hz=PASS | MouseVsTabletFilter=PASS | AntiOvershoot=PASS | SharedVidPid=PASS | RingBuffer100k=PASS | AtomicRestore=PASS | MissingKeysByteExact=PASS | PartialKeys=PASS",
                    db.tablets.Count,
                    db.glossary.Count));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("SELF-TEST EXCEPTION: " + ex);
                return 1;
            }
        }

        private static int RunUiSmokeTest()
        {
            try
            {
                try { Console.OutputEncoding = Encoding.UTF8; } catch { }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using (MainForm form = new MainForm())
                {
                    form.Show();
                    Application.DoEvents();

                    string overlapReport;
                    bool noOverlaps = form.CheckNoOverlappingControls(out overlapReport);

                    string previewPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ui_preview.png");
                    using (Bitmap bmp = new Bitmap(form.Width, form.Height, PixelFormat.Format32bppArgb))
                    {
                        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                        bmp.Save(previewPath, ImageFormat.Png);
                    }

                    form.Close();

                    if (!noOverlaps)
                    {
                        Console.Error.WriteLine("UI-SMOKE-TEST FAIL: Overlapping controls detected -> " + overlapReport);
                        return 1;
                    }

                    Console.WriteLine(string.Format(
                        "UI-SMOKE-TEST OK | ClientSize={0}x{1} | Overlaps=0 | Preview={2}",
                        form.ClientSize.Width,
                        form.ClientSize.Height,
                        previewPath));
                }
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("UI-SMOKE-TEST EXCEPTION: " + ex);
                return 1;
            }
        }
    }
}
