using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace MeinyaTabletBooster
{
    public sealed class WacomConfigState
    {
        public bool Exists { get; set; }
        public bool HasOriginalBackup { get; set; }
        public int PacketRate { get; set; }
        public bool MaxDataRateOn { get; set; }
        public bool WinUseInk { get; set; }
        public bool DisableDoubleClickAssist { get; set; }

        public WacomConfigState()
        {
            PacketRate = 100;
            MaxDataRateOn = true;
            WinUseInk = true;
            DisableDoubleClickAssist = false;
        }
    }

    /// <summary>
    /// INV-4 & INV-5: Quản lý file cấu hình Wacom_Tablet.dat với cơ chế:
    /// 1. Khóa liên tiến trình (Mutex) bảo vệ toàn bộ chuỗi đọc - tạo bản sao lưu gốc - ghi cấu hình.
    /// 2. Bản sao lưu gốc bất biến (.original_backup chỉ xuất bản bằng File.Move không ghi đè).
    /// 3. Ghi nguyên tử qua file tạm (.tmp -> flush -> kiểm tra -> File.Replace nguyên tử vào file đích -> xóa .tmp).
    /// </summary>
    public static class WacomConfigManager
    {
        private const string ConfigMutexName = @"Local\MeinyaTabletBooster_WacomConfigMutex_v2";

        public static string DefaultDatPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WTablet",
                    "Wacom_Tablet.dat");
            }
        }

        public static string GetOriginalBackupPath(string datPath)
        {
            return datPath + ".original_backup";
        }

        private static void ExecuteWithConfigLock(Action action)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(false, ConfigMutexName, out createdNew))
            {
                bool acquired = false;
                try
                {
                    try
                    {
                        acquired = mutex.WaitOne(5000);
                    }
                    catch (AbandonedMutexException)
                    {
                        acquired = true;
                    }
                    action();
                }
                finally
                {
                    if (acquired)
                    {
                        try { mutex.ReleaseMutex(); } catch { }
                    }
                }
            }
        }

        public static WacomConfigState ReadConfig(string customDatPath = null)
        {
            string datPath = string.IsNullOrEmpty(customDatPath) ? DefaultDatPath : customDatPath;
            string backupPath = GetOriginalBackupPath(datPath);

            WacomConfigState state = new WacomConfigState
            {
                Exists = File.Exists(datPath),
                HasOriginalBackup = File.Exists(backupPath)
            };

            if (!state.Exists) return state;

            string content = File.ReadAllText(datPath, Encoding.UTF8);

            Match mRate = Regex.Match(content, @"<PacketRate\s+type=""integer"">(\d+)</PacketRate>");
            int pr;
            if (mRate.Success && int.TryParse(mRate.Groups[1].Value, out pr))
            {
                state.PacketRate = pr;
            }

            Match mMax = Regex.Match(content, @"<MaxDataRateOn\s+type=""bool"">(true|false)</MaxDataRateOn>", RegexOptions.IgnoreCase);
            if (mMax.Success)
            {
                state.MaxDataRateOn = string.Equals(mMax.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            }

            Match mInk = Regex.Match(content, @"<WinUseInk\s+type=""bool"">(true|false)</WinUseInk>", RegexOptions.IgnoreCase);
            if (mInk.Success)
            {
                state.WinUseInk = string.Equals(mInk.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase);
            }

            Match mDbl = Regex.Match(content, @"<DoubleClickDistance\s+type=""double"">([0-9.]+)</DoubleClickDistance>");
            double dblVal;
            if (mDbl.Success && double.TryParse(mDbl.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out dblVal))
            {
                state.DisableDoubleClickAssist = (dblVal <= 0.05);
            }

            return state;
        }

        /// <summary>
        /// INV-4: Chỉ tạo file sao lưu gốc nếu chưa tồn tại bằng thao tác đổi tên không ghi đè (allowOverwrite: false).
        /// </summary>
        public static void EnsureOriginalBackup(string datPath)
        {
            if (!File.Exists(datPath))
            {
                throw new FileNotFoundException("Không tìm thấy file cấu hình Wacom_Tablet.dat: " + datPath);
            }

            string backupPath = GetOriginalBackupPath(datPath);
            if (!File.Exists(backupPath))
            {
                byte[] originalBytes = File.ReadAllBytes(datPath);
                WriteBytesAtomically(backupPath, originalBytes, false);
            }
        }

        /// <summary>
        /// INV-5: Ghi nguyên tử qua file tạm .tmp, flush xuống đĩa, kiểm tra dung lượng rồi dùng File.Replace (nếu đích đã có)
        /// hoặc File.Move (nếu đích chưa có) để tráo nguyên tử trên NTFS.
        /// Khi allowOverwrite = false (dùng cho .original_backup), tuyệt đối không ghi đè nếu file đích đã tồn tại.
        /// </summary>
        public static void WriteBytesAtomically(string targetPath, byte[] data, bool allowOverwrite = true)
        {
            if (string.IsNullOrEmpty(targetPath)) throw new ArgumentNullException("targetPath");
            if (data == null || data.Length == 0) throw new InvalidOperationException("Dữ liệu cấu hình rỗng, từ chối ghi.");

            string dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!allowOverwrite && File.Exists(targetPath))
            {
                return;
            }

            string tmpPath = targetPath + ".tmp." + Process.GetCurrentProcess().Id + "." + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }

                FileInfo fi = new FileInfo(tmpPath);
                if (!fi.Exists || fi.Length != data.Length)
                {
                    throw new IOException("Kiểm tra toàn vẹn file tạm thất bại trước khi tráo nguyên tử.");
                }

                if (!allowOverwrite)
                {
                    if (!File.Exists(targetPath))
                    {
                        try
                        {
                            File.Move(tmpPath, targetPath);
                        }
                        catch (IOException)
                        {
                            // Một tiến trình khác vừa tạo xong backup cùng lúc -> giữ nguyên bản đầu tiên
                        }
                    }
                }
                else
                {
                    if (File.Exists(targetPath))
                    {
                        File.Replace(tmpPath, targetPath, null, true);
                    }
                    else
                    {
                        File.Move(tmpPath, targetPath);
                    }
                }
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
        }

        public static string ApplyOptimizedConfig(
            int packetRate,
            bool maxDataRateOn,
            bool winUseInk,
            bool disableDoubleClickDistance,
            string customDatPath = null)
        {
            string datPath = string.IsNullOrEmpty(customDatPath) ? DefaultDatPath : customDatPath;
            StringBuilder skippedKeys = new StringBuilder();
            ExecuteWithConfigLock(() =>
            {
                if (!File.Exists(datPath))
                {
                    throw new FileNotFoundException("Không tìm thấy file Wacom_Tablet.dat tại: " + datPath);
                }

                // INV-4: Đảm bảo bản sao lưu gốc đã được khóa an toàn trước khi sửa
                EnsureOriginalBackup(datPath);

                string content = File.ReadAllText(datPath, Encoding.UTF8);
                string originalContent = content;

                if (Regex.IsMatch(content, @"<PacketRate\s+type=""integer"">\d+</PacketRate>"))
                {
                    content = Regex.Replace(
                        content,
                        @"(<PacketRate\s+type=""integer"">)\d+(</PacketRate>)",
                        "${1}" + packetRate.ToString(CultureInfo.InvariantCulture) + "${2}");
                }
                else
                {
                    skippedKeys.AppendLine("driver này không có khóa PacketRate, không sửa (không tìm thấy dạng khóa được hỗ trợ).");
                }

                string maxStr = maxDataRateOn ? "true" : "false";
                if (Regex.IsMatch(content, @"<MaxDataRateOn\s+type=""bool"">(true|false)</MaxDataRateOn>", RegexOptions.IgnoreCase))
                {
                    content = Regex.Replace(
                        content,
                        @"(<MaxDataRateOn\s+type=""bool"">)(true|false)(</MaxDataRateOn>)",
                        "${1}" + maxStr + "${3}",
                        RegexOptions.IgnoreCase);
                }
                else
                {
                    skippedKeys.AppendLine("driver này không có khóa MaxDataRateOn, không sửa (không tìm thấy dạng khóa được hỗ trợ).");
                }

                string inkStr = winUseInk ? "true" : "false";
                if (!Regex.IsMatch(content, @"<WinUseInk\s+type=""bool"">(true|false)</WinUseInk>", RegexOptions.IgnoreCase))
                {
                    skippedKeys.AppendLine("driver này không có khóa WinUseInk, không sửa (không tìm thấy dạng khóa được hỗ trợ).");
                }
                content = Regex.Replace(
                    content,
                    @"(<WinUseInk\s+type=""bool"">)(true|false)(</WinUseInk>)",
                    "${1}" + inkStr + "${3}",
                    RegexOptions.IgnoreCase);

                string dblStr = disableDoubleClickDistance ? "0.000000" : "2.000000";
                if (!Regex.IsMatch(content, @"<DoubleClickDistance\s+type=""double"">[0-9.]+</DoubleClickDistance>"))
                {
                    skippedKeys.AppendLine("driver này không có khóa DoubleClickDistance, không sửa (không tìm thấy dạng khóa được hỗ trợ).");
                }
                content = Regex.Replace(
                    content,
                    @"(<DoubleClickDistance\s+type=""double"">)[0-9.]+(</DoubleClickDistance>)",
                    "${1}" + dblStr + "${2}");

                // Không ghi lại khi không có thay đổi: giữ cả BOM và kiểu xuống dòng gốc.
                if (!string.Equals(content, originalContent, StringComparison.Ordinal))
                {
                    byte[] outBytes = new UTF8Encoding(false).GetBytes(content);
                    WriteBytesAtomically(datPath, outBytes, true);
                }
            });
            return skippedKeys.ToString();
        }

        public static void RestoreOriginalBackup(string customDatPath = null)
        {
            string datPath = string.IsNullOrEmpty(customDatPath) ? DefaultDatPath : customDatPath;
            ExecuteWithConfigLock(() =>
            {
                string backupPath = GetOriginalBackupPath(datPath);
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException("Chưa có bản sao lưu gốc (.original_backup) tại: " + backupPath);
                }

                byte[] originalBytes = File.ReadAllBytes(backupPath);
                WriteBytesAtomically(datPath, originalBytes, true);
            });
        }

        public static bool IsRunningAsAdministrator()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool TryRestartWacomService(out string statusMessage)
        {
            if (!IsRunningAsAdministrator())
            {
                statusMessage = "Đã lưu cấu hình an toàn. Để Wacom Driver nạp lại ngay lập tức không cần khởi động lại máy, bạn có thể bấm nút 'Mở lại Tool với Quyền Admin'.";
                return false;
            }

            try
            {
                int stopCode;
                bool stopDone = RunHiddenCmd("net stop WTabletServicePro", 7000, out stopCode);
                int startCode;
                bool startDone = RunHiddenCmd("net start WTabletServicePro", 7000, out startCode);

                if (stopDone && startDone && startCode == 0)
                {
                    statusMessage = "Đã lưu cấu hình và khởi động lại dịch vụ WTabletServicePro thành công!";
                    return true;
                }

                statusMessage = "Đã lưu file cấu hình an toàn, nhưng không thể tự khởi động lại WTabletServicePro (mã thoát: " + startCode + "). Bạn có thể rút cáp USB bảng vẽ cắm lại hoặc khởi động lại máy.";
                return false;
            }
            catch (Exception ex)
            {
                statusMessage = "Đã lưu file cấu hình, nhưng gặp lỗi khi gọi dịch vụ: " + ex.Message;
                return false;
            }
        }

        private static bool RunHiddenCmd(string cmdArgs, int timeoutMs, out int exitCode)
        {
            exitCode = -1;
            ProcessStartInfo psi = new ProcessStartInfo("cmd.exe", "/c " + cmdArgs)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process p = Process.Start(psi))
            {
                if (p == null) return false;
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return false;
                }
                exitCode = p.ExitCode;
                return true;
            }
        }
    }
}
