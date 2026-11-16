using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace MeinyaTabletBooster
{
    public struct CanvasSamplePoint
    {
        public float X;
        public float Y;
        public bool IsInterpolated;
        public long Tick;
    }

    /// <summary>
    /// INV-2: Bộ đệm vòng kích thước cố định (Zero-Allocation Fixed Ring Buffer).
    /// Cấp phát đúng 1 lần duy nhất lúc khởi tạo. Khi đầy tự xoay vòng ghi đè lên phần tử cũ nhất.
    /// Không bao giờ sinh thêm rác bộ nhớ (0 byte cấp phát mới) dù chạy liên tục nhiều ngày.
    /// </summary>
    public sealed class FixedRingBuffer<T> where T : struct
    {
        private readonly T[] _buffer;
        private readonly int _capacity;
        private readonly int _mask;
        private int _writeIndex;
        private int _count;
        private long _totalOverwrittenCount;
        private readonly object _sync = new object();

        public int Capacity { get { return _capacity; } }

        public int Count
        {
            get { lock (_sync) { return _count; } }
        }

        public long TotalOverwrittenCount
        {
            get { lock (_sync) { return _totalOverwrittenCount; } }
        }

        public FixedRingBuffer(int powerOfTwoCapacity)
        {
            if (powerOfTwoCapacity < 2 || (powerOfTwoCapacity & (powerOfTwoCapacity - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a power of two.", "powerOfTwoCapacity");
            }
            _capacity = powerOfTwoCapacity;
            _mask = powerOfTwoCapacity - 1;
            _buffer = new T[powerOfTwoCapacity];
            _writeIndex = 0;
            _count = 0;
            _totalOverwrittenCount = 0;
        }

        public void Push(T item)
        {
            lock (_sync)
            {
                _buffer[_writeIndex] = item;
                _writeIndex = (_writeIndex + 1) & _mask;
                if (_count < _capacity)
                {
                    _count++;
                }
                else
                {
                    _totalOverwrittenCount++;
                }
            }
        }

        /// <summary>
        /// Sao chép dữ liệu theo thứ tự thời gian (từ cũ nhất đến mới nhất) vào mảng đích có sẵn,
        /// không cấp phát mảng mới trên Heap.
        /// </summary>
        public int CopyChronologicalTo(T[] destination)
        {
            if (destination == null) return 0;
            lock (_sync)
            {
                int toCopy = Math.Min(_count, destination.Length);
                int startIdx = (_writeIndex - toCopy) & _mask;
                for (int i = 0; i < toCopy; i++)
                {
                    destination[i] = _buffer[(startIdx + i) & _mask];
                }
                return toCopy;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                Array.Clear(_buffer, 0, _capacity);
                _writeIndex = 0;
                _count = 0;
            }
        }
    }

    public struct TelemetrySnapshot
    {
        public double RawHardwareHz;
        public double EffectiveOutputHz;
        public double AvgIntervalMs;
        public double PeakRawHz;
        public int RingBufferActiveItems;
        public int RingBufferMaxCapacity;
        public long RingBufferRecycledCount;
        public long ManagedHeapBytes;
        public bool VelocityBufferFlushed;
        public long IgnoredMouseEventsCount;
        public long TabletEventsCount;
        public string ActiveInputSourceLabel;
        public double JitterMs;
    }

    /// <summary>
    /// INV-1, INV-2, INV-3: Bộ đo tần số sự kiện thực tế, lọc phân biệt Chuột rời vs Bảng vẽ/Bút (Windows Ink & Driver Inject),
    /// nội suy thời gian thực chống văng lố trên CPU và tự động thu dọn/xả đệm bộ nhớ (TemporalResampler & MemoryGuard).
    /// </summary>
    public sealed class TemporalResamplerEngine : IDisposable
    {
        public const long InjectedSignature = 0x4D45494E; // "MEIN"
        public const long WindowsPenSignatureMask = 0xFFFFFF00L;
        public const long WindowsPenSignatureValue = 0xFF515700L; // MI_WP_SIGNATURE (Pen)
        public const int IntervalRingCapacity = 256;
        public const int CanvasRingCapacity = 512;
        public const double IdleVelocityFlushMs = 180.0;
        public const double JumpTeleportThresholdPx = 80.0;
        public const double QuantizationDeadbandPx = 1.25;

        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200;
        private const uint LLMHF_INJECTED = 0x00000001;
        private const uint LLMHF_LOWER_IL_INJECTED = 0x00000002;
        private const uint INPUT_MOUSE = 0;
        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
        private const uint TIMER_ALL_ACCESS = 0x1F0003;

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public MOUSEINPUT mi;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

        [DllImport("winmm.dll")]
        private static extern uint timeBeginPeriod(uint uPeriod);

        [DllImport("winmm.dll")]
        private static extern uint timeEndPeriod(uint uPeriod);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWaitableTimerExW(IntPtr lpTimerAttributes, string lpTimerName, uint dwFlags, uint dwDesiredAccess);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(IntPtr hTimer, ref long pDueTime, int lPeriod, IntPtr pfnCompletionRoutine, IntPtr lpArgToCompletionRoutine, [MarshalAs(UnmanagedType.Bool)] bool fResume);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        private readonly FixedRingBuffer<double> _rawIntervalMsBuffer = new FixedRingBuffer<double>(IntervalRingCapacity);
        private readonly FixedRingBuffer<double> _outIntervalMsBuffer = new FixedRingBuffer<double>(IntervalRingCapacity);
        private readonly FixedRingBuffer<CanvasSamplePoint> _canvasRingBuffer = new FixedRingBuffer<CanvasSamplePoint>(CanvasRingCapacity);
        private readonly double[] _tempIntervalsScratch = new double[IntervalRingCapacity];
        private readonly object _stateLock = new object();

        private LowLevelMouseProc _hookProcDelegate;
        private IntPtr _hookHandle = IntPtr.Zero;
        private Thread _workerThread;
        private volatile bool _isDisposed;
        private volatile bool _resamplerEnabled;
        private volatile int _targetHz = 330;
        private volatile int _smoothingPercent = 35;
        private bool _timerPeriodActive;

        private long _lastTabletDriverTick;
        private long _lastRawTick;
        private long _lastOutTick;
        private long _ignoredMouseEventsCount;
        private long _tabletEventsCount;
        private string _lastActiveSource = "Chờ tín hiệu";
        private double _peakRawHz;
        private double _emaIntervalMs = 7.52;
        private double _remainingStepBudgetPx;
        private double _prevRawX;
        private double _prevRawY;
        private double _currRawX;
        private double _currRawY;
        private double _velocityXPerMs;
        private double _velocityYPerMs;
        private double _subPixelRemainderX;
        private double _subPixelRemainderY;
        private double _velocityDerivX;
        private double _velocityDerivY;
        private long _lastSelfInjectedTick;
        private volatile bool _hasTabletContext = true;
        private bool _hasValidVelocity;

        public FixedRingBuffer<CanvasSamplePoint> CanvasBuffer { get { return _canvasRingBuffer; } }

        public bool HasTabletContext
        {
            get { return _hasTabletContext; }
            set { _hasTabletContext = value; }
        }

        public bool WasRecentSelfInjected(long nowTicks)
        {
            lock (_stateLock)
            {
                if (_lastSelfInjectedTick <= 0 || nowTicks < _lastSelfInjectedTick) return false;
                double elapsedMs = (nowTicks - _lastSelfInjectedTick) * (1000.0 / Stopwatch.Frequency);
                return elapsedMs <= 4.0;
            }
        }

        private static double ComputeLowPassAlpha(double dtSec, double cutoffHz)
        {
            if (dtSec <= 1e-6 || cutoffHz <= 1e-3) return 1.0;
            double tau = 1.0 / (2.0 * Math.PI * cutoffHz);
            return 1.0 / (1.0 + tau / dtSec);
        }

        public bool IsResamplerEnabled
        {
            get { return _resamplerEnabled; }
        }

        public int TargetHz
        {
            get { return _targetHz; }
            set { _targetHz = Math.Max(60, Math.Min(1000, value)); }
        }

        public int SmoothingPercent
        {
            get { return _smoothingPercent; }
            set { _smoothingPercent = Math.Max(0, Math.Min(90, value)); }
        }

        public double RemainingStepBudgetPx
        {
            get { lock (_stateLock) { return _remainingStepBudgetPx; } }
        }

        public void StartHookAndEngine()
        {
            if (_hookHandle == IntPtr.Zero)
            {
                _hookProcDelegate = HookCallback;
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    IntPtr hMod = GetModuleHandle(curModule != null ? curModule.ModuleName : null);
                    _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _hookProcDelegate, hMod, 0);
                }
            }

            if (_workerThread == null)
            {
                _workerThread = new Thread(ResamplerWorkerLoop)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Highest,
                    Name = "MeinyaTemporalResampler"
                };
                _workerThread.Start();
            }
        }

        public void SetResamplerActive(bool enable)
        {
            lock (_stateLock)
            {
                if (_resamplerEnabled == enable) return;
                _resamplerEnabled = enable;
                ResetVelocityStateUnlocked();
                if (enable && !_timerPeriodActive)
                {
                    try { timeBeginPeriod(1); _timerPeriodActive = true; } catch { }
                }
                else if (!enable && _timerPeriodActive)
                {
                    try { timeEndPeriod(1); _timerPeriodActive = false; } catch { }
                }
            }
        }

        /// <summary>
        /// INV-3: Xóa sạch trạng thái vận tốc tạm thời khi nhấc bút hoặc ngừng lia bút > 180ms.
        /// </summary>
        public void ResetVelocityStateUnlocked()
        {
            _velocityXPerMs = 0.0;
            _velocityYPerMs = 0.0;
            _velocityDerivX = 0.0;
            _velocityDerivY = 0.0;
            _subPixelRemainderX = 0.0;
            _subPixelRemainderY = 0.0;
            _remainingStepBudgetPx = 0.0;
            _hasValidVelocity = false;
        }

        /// <summary>
        /// INV-2: Chủ động dọn sạch toàn bộ bộ đệm vòng và thu hồi bộ nhớ rác trên Heap.
        /// </summary>
        public void CleanAllBuffersAndCompactRam()
        {
            lock (_stateLock)
            {
                _rawIntervalMsBuffer.Clear();
                _outIntervalMsBuffer.Clear();
                _canvasRingBuffer.Clear();
                _peakRawHz = 0.0;
                _emaIntervalMs = 7.52;
                _lastRawTick = 0;
                _lastOutTick = 0;
                _lastTabletDriverTick = 0;
                _lastSelfInjectedTick = 0;
                _ignoredMouseEventsCount = 0;
                _tabletEventsCount = 0;
                _lastActiveSource = "Chờ tín hiệu";
                ResetVelocityStateUnlocked();
            }
            GC.Collect(1, GCCollectionMode.Optimized, false);
        }

        /// <summary>
        /// Xử lý 1 điểm tọa độ phần cứng gốc (dùng cho cả Hook thực tế và kiểm thử tự động --self-test).
        /// </summary>
        public void ProcessRawHardwareSample(int x, int y, long timestampTicks)
        {
            double tickToMs = 1000.0 / Stopwatch.Frequency;
            lock (_stateLock)
            {
                _tabletEventsCount++;
                if (_lastRawTick > 0)
                {
                    double dtMs = (timestampTicks - _lastRawTick) * tickToMs;
                    double outDtMs = (_lastOutTick > 0) ? ((timestampTicks - _lastOutTick) * tickToMs) : dtMs;

                    if (dtMs >= 0.4 && dtMs <= 160.0)
                    {
                        _rawIntervalMsBuffer.Push(dtMs);
                        if (outDtMs >= 0.2 && outDtMs <= 160.0)
                        {
                            _outIntervalMsBuffer.Push(outDtMs);
                        }

                        double instHz = 1000.0 / dtMs;
                        if (instHz <= 1200.0 && instHz > _peakRawHz)
                        {
                            _peakRawHz = instHz;
                        }

                        double dx = x - _currRawX;
                        double dy = y - _currRawY;
                        double dist = Math.Sqrt(dx * dx + dy * dy);

                        // INV-3: Nếu nhấc bút đặt sang vị trí mới xa (>80px) hoặc sau khoảng nghỉ (>65ms), xóa vận tốc cũ ngay
                        if (dist > 0.0 && dist <= JumpTeleportThresholdPx && dtMs <= 65.0)
                        {
                            _emaIntervalMs = (_emaIntervalMs <= 0.1) ? dtMs : (_emaIntervalMs * 0.8 + dtMs * 0.2);
                            double instVx = dx / dtMs;
                            double instVy = dy / dtMs;

                            if (_hasValidVelocity && ((dx * _velocityXPerMs + dy * _velocityYPerMs) <= 0.0))
                            {
                                // Cua gấp >= 90 độ hoặc đảo chiều: bám ngay hướng mới để tránh văng lố
                                _velocityXPerMs = instVx;
                                _velocityYPerMs = instVy;
                                _velocityDerivX = 0.0;
                                _velocityDerivY = 0.0;
                                _subPixelRemainderX = 0.0;
                                _subPixelRemainderY = 0.0;
                            }
                            else if (_hasValidVelocity)
                            {
                                double dtSec = dtMs / 1000.0;
                                double deltaPxX = Math.Abs(instVx - _velocityXPerMs) * dtMs;
                                double deltaPxY = Math.Abs(instVy - _velocityYPerMs) * dtMs;
                                double effDvX = (deltaPxX <= QuantizationDeadbandPx) ? 0.0 : (((deltaPxX - QuantizationDeadbandPx) / dtMs) / dtSec);
                                double effDvY = (deltaPxY <= QuantizationDeadbandPx) ? 0.0 : (((deltaPxY - QuantizationDeadbandPx) / dtMs) / dtSec);
                                double alphaD = ComputeLowPassAlpha(dtSec, 8.0);
                                _velocityDerivX = alphaD * effDvX + (1.0 - alphaD) * _velocityDerivX;
                                _velocityDerivY = alphaD * effDvY + (1.0 - alphaD) * _velocityDerivY;

                                double minCutoffHz = Math.Max(1.2, 6.0 - (_smoothingPercent / 100.0) * 4.2);
                                const double beta = 22.0;
                                double alphaVx = ComputeLowPassAlpha(dtSec, minCutoffHz + beta * _velocityDerivX);
                                double alphaVy = ComputeLowPassAlpha(dtSec, minCutoffHz + beta * _velocityDerivY);
                                _velocityXPerMs = alphaVx * instVx + (1.0 - alphaVx) * _velocityXPerMs;
                                _velocityYPerMs = alphaVy * instVy + (1.0 - alphaVy) * _velocityYPerMs;
                            }
                            else
                            {
                                _velocityXPerMs = instVx;
                                _velocityYPerMs = instVy;
                                _velocityDerivX = 0.0;
                                _velocityDerivY = 0.0;
                            }
                            _hasValidVelocity = true;
                            double rawBudget = dist * (0.35 + (_smoothingPercent / 100.0) * 0.55);
                            _remainingStepBudgetPx = (dist < 1.5)
                                ? Math.Min(1.0, rawBudget)
                                : Math.Min(12.0, Math.Max(1.0, rawBudget));
                        }
                        else
                        {
                            ResetVelocityStateUnlocked();
                        }
                    }
                    else
                    {
                        // INV-3: Mọi khoảng nghỉ ngoài [0.4, 160.0] ms đều xóa sạch vận tốc cũ (không để hở khoảng 160-180ms)
                        ResetVelocityStateUnlocked();
                    }
                }

                _prevRawX = _currRawX;
                _prevRawY = _currRawY;
                _currRawX = x;
                _currRawY = y;
                _lastRawTick = timestampTicks;
                _lastOutTick = timestampTicks;
            }
        }

        public bool EvaluateHookMouseMove(long extraInfo, uint flags, long nowTicks, out bool isSelfInjected)
        {
            // INV-1: Bỏ qua hoàn toàn các gói tọa độ do chính bộ nội suy bơm vào để tránh lặp vô tận
            long extraLow32 = extraInfo & 0xFFFFFFFFL;
            isSelfInjected = (extraInfo == InjectedSignature) || (extraLow32 == InjectedSignature);
            if (isSelfInjected)
            {
                lock (_stateLock)
                {
                    _lastSelfInjectedTick = nowTicks;
                }
                return false;
            }

            bool hasPenInkSig = ((extraLow32 & WindowsPenSignatureMask) == WindowsPenSignatureValue);
            if (hasPenInkSig)
            {
                lock (_stateLock)
                {
                    _lastTabletDriverTick = nowTicks;
                    _lastActiveSource = "Bút (Windows Ink)";
                }
                return true;
            }

            bool isDriverInjected = ((flags & LLMHF_INJECTED) != 0) || ((flags & LLMHF_LOWER_IL_INJECTED) != 0);
            if (isDriverInjected && _hasTabletContext)
            {
                lock (_stateLock)
                {
                    _lastTabletDriverTick = nowTicks;
                    _lastActiveSource = "Bảng vẽ (Driver Tablet)";
                }
                return true;
            }

            double tickToMs = 1000.0 / Stopwatch.Frequency;
            lock (_stateLock)
            {
                double sinceTabletDriverMs = (_lastTabletDriverTick > 0) ? ((nowTicks - _lastTabletDriverTick) * tickToMs) : 99999.0;
                if (sinceTabletDriverMs <= 2500.0)
                {
                    _ignoredMouseEventsCount++;
                    _lastActiveSource = "Đã lọc Chuột rời";
                    if (_hasValidVelocity)
                    {
                        ResetVelocityStateUnlocked();
                    }
                    return false;
                }

                _lastActiveSource = "Con trỏ HID";
                return true;
            }
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && wParam == (IntPtr)WM_MOUSEMOVE)
            {
                MSLLHOOKSTRUCT info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                long extra = info.dwExtraInfo.ToInt64();
                long now = Stopwatch.GetTimestamp();
                bool isSelfInjected;
                if (EvaluateHookMouseMove(extra, info.flags, now, out isSelfInjected))
                {
                    ProcessRawHardwareSample(info.pt.X, info.pt.Y, now);
                }
            }
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void ResamplerWorkerLoop()
        {
            double tickToMs = 1000.0 / Stopwatch.Frequency;
            INPUT[] inputBuffer = new INPUT[1];
            inputBuffer[0].type = INPUT_MOUSE;
            inputBuffer[0].mi.dwFlags = MOUSEEVENTF_MOVE;
            inputBuffer[0].mi.dwExtraInfo = (IntPtr)InjectedSignature;
            int inputStructSize = Marshal.SizeOf(typeof(INPUT));

            IntPtr waitTimer = IntPtr.Zero;
            try
            {
                waitTimer = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
            }
            catch { }

            try
            {
                while (!_isDisposed)
                {
                    if (!_resamplerEnabled)
                    {
                        Thread.Sleep(15);
                        continue;
                    }

                    int targetHz = _targetHz;
                    double targetPeriodMs = 1000.0 / Math.Max(60, targetHz);
                    long nowTick = Stopwatch.GetTimestamp();

                    int moveDx = 0;
                    int moveDy = 0;
                    bool shouldSend = false;
                    double waitRemainingMs = 0.0;
                    bool isIdleWait = false;

                    lock (_stateLock)
                    {
                        double sinceRawMs = (_lastRawTick > 0) ? ((nowTick - _lastRawTick) * tickToMs) : 9999.0;
                        double sinceOutMs = (_lastOutTick > 0) ? ((nowTick - _lastOutTick) * tickToMs) : 9999.0;
                        double maxHorizonMs = Math.Max(4.5, Math.Min(12.0, (_emaIntervalMs > 1.0 ? _emaIntervalMs : 8.0) * 1.25));

                        // INV-3: Tự xả sạch bộ đệm vận tốc khi bút ngừng di chuyển quá 180ms
                        if (sinceRawMs > IdleVelocityFlushMs)
                        {
                            if (_hasValidVelocity)
                            {
                                ResetVelocityStateUnlocked();
                            }
                            waitRemainingMs = 2.0;
                        }
                        else if (sinceOutMs < targetPeriodMs)
                        {
                            // Lập lịch tổng đầu ra (gồm cả gói gốc + gói nội suy) đúng theo nhịp TargetHz
                            waitRemainingMs = targetPeriodMs - sinceOutMs;
                        }
                        else if (_hasValidVelocity && sinceRawMs >= 0.8 && sinceRawMs <= maxHorizonMs && _remainingStepBudgetPx > 0.25 && _smoothingPercent > 0)
                        {
                            double stepDt = Math.Min(sinceOutMs, 6.0);
                            double damping = Math.Max(0.15, 1.0 - (sinceRawMs / (maxHorizonMs + 2.0)));
                            double blendFactor = (0.20 + (_smoothingPercent / 100.0) * 0.65) * damping;

                            double rawStepX = _velocityXPerMs * stepDt * blendFactor + _subPixelRemainderX;
                            double rawStepY = _velocityYPerMs * stepDt * blendFactor + _subPixelRemainderY;

                            // Kẹp biên an toàn: không bao giờ dịch quá 6px hoặc vượt ngân sách bước còn lại (Anti-Overshoot)
                            rawStepX = Math.Max(-6.0, Math.Min(6.0, rawStepX));
                            rawStepY = Math.Max(-6.0, Math.Min(6.0, rawStepY));

                            double rawLen = Math.Sqrt(rawStepX * rawStepX + rawStepY * rawStepY);
                            if (rawLen > _remainingStepBudgetPx && rawLen > 1e-6)
                            {
                                double scale = _remainingStepBudgetPx / rawLen;
                                rawStepX *= scale;
                                rawStepY *= scale;
                            }

                            moveDx = (int)Math.Round(rawStepX);
                            moveDy = (int)Math.Round(rawStepY);
                            double stepLen = Math.Sqrt((double)moveDx * moveDx + (double)moveDy * moveDy);

                            if (stepLen > _remainingStepBudgetPx)
                            {
                                moveDx = 0;
                                moveDy = 0;
                                _remainingStepBudgetPx = 0.0;
                                _subPixelRemainderX = 0.0;
                                _subPixelRemainderY = 0.0;
                                waitRemainingMs = 2.0;
                            }
                            else
                            {
                                _subPixelRemainderX = rawStepX - moveDx;
                                _subPixelRemainderY = rawStepY - moveDy;

                                if (moveDx != 0 || moveDy != 0)
                                {
                                    _remainingStepBudgetPx = Math.Max(0.0, _remainingStepBudgetPx - stepLen);
                                    shouldSend = true;
                                }
                                else
                                {
                                    waitRemainingMs = 2.0;
                                }
                            }
                        }
                        else if (_hasValidVelocity && sinceRawMs < 0.8)
                        {
                            waitRemainingMs = 0.8 - sinceRawMs;
                        }
                        else
                        {
                            waitRemainingMs = 2.0;
                        }
                        isIdleWait = !_hasValidVelocity;
                    }

                    if (waitRemainingMs > 0.0)
                    {
                        if (isIdleWait && waitRemainingMs >= 1.8)
                        {
                            Thread.Sleep(1);
                        }
                        else
                        {
                            long deadlineTick = Stopwatch.GetTimestamp() + (long)Math.Round(waitRemainingMs / tickToMs);
                            if (waitRemainingMs > 0.85 && waitTimer != IntPtr.Zero)
                            {
                                long due100Ns = -(long)Math.Round((waitRemainingMs - 0.55) * 10000.0);
                                if (due100Ns < -1000)
                                {
                                    SetWaitableTimer(waitTimer, ref due100Ns, 0, IntPtr.Zero, IntPtr.Zero, false);
                                    WaitForSingleObject(waitTimer, 4);
                                }
                            }
                            while (!_isDisposed && Stopwatch.GetTimestamp() < deadlineTick)
                            {
                                Thread.SpinWait(40);
                            }
                        }
                        continue;
                    }

                    if (shouldSend)
                    {
                        inputBuffer[0].mi.dx = moveDx;
                        inputBuffer[0].mi.dy = moveDy;
                        uint sent = SendInput(1, inputBuffer, inputStructSize);
                        if (sent == 1)
                        {
                            long sentTick = Stopwatch.GetTimestamp();
                            lock (_stateLock)
                            {
                                if (_lastOutTick > 0)
                                {
                                    double outDt = (sentTick - _lastOutTick) * tickToMs;
                                    if (outDt >= 0.2 && outDt <= 160.0)
                                    {
                                        _outIntervalMsBuffer.Push(outDt);
                                    }
                                }
                                _lastOutTick = sentTick;
                            }
                        }
                    }
                    else
                    {
                        Thread.Sleep(1);
                    }
                }
            }
            finally
            {
                if (waitTimer != IntPtr.Zero)
                {
                    try { CloseHandle(waitTimer); } catch { }
                }
            }
        }

        public TelemetrySnapshot GetTelemetrySnapshot()
        {
            double tickToMs = 1000.0 / Stopwatch.Frequency;
            long nowTick = Stopwatch.GetTimestamp();
            TelemetrySnapshot snap = new TelemetrySnapshot();

            lock (_stateLock)
            {
                double sinceRawMs = (_lastRawTick > 0) ? ((nowTick - _lastRawTick) * tickToMs) : 9999.0;
                if (sinceRawMs > IdleVelocityFlushMs && _hasValidVelocity)
                {
                    ResetVelocityStateUnlocked();
                }

                snap.VelocityBufferFlushed = !_hasValidVelocity;
                snap.PeakRawHz = _peakRawHz;
                snap.RingBufferActiveItems = _rawIntervalMsBuffer.Count + _canvasRingBuffer.Count;
                snap.RingBufferMaxCapacity = IntervalRingCapacity + CanvasRingCapacity;
                snap.RingBufferRecycledCount = _rawIntervalMsBuffer.TotalOverwrittenCount + _canvasRingBuffer.TotalOverwrittenCount;
                snap.IgnoredMouseEventsCount = _ignoredMouseEventsCount;
                snap.TabletEventsCount = _tabletEventsCount;
                snap.ActiveInputSourceLabel = _lastActiveSource ?? "Chờ tín hiệu";

                if (sinceRawMs <= 300.0 && _rawIntervalMsBuffer.Count >= 3)
                {
                    int n = _rawIntervalMsBuffer.CopyChronologicalTo(_tempIntervalsScratch);
                    int take = Math.Min(n, 32);
                    double sum = 0.0;
                    for (int i = n - take; i < n; i++)
                    {
                        sum += _tempIntervalsScratch[i];
                    }
                    double avgMs = sum / take;
                    if (avgMs > 0.1)
                    {
                        snap.AvgIntervalMs = avgMs;
                        snap.RawHardwareHz = 1000.0 / avgMs;
                        double sqDiffSum = 0.0;
                        for (int i = n - take; i < n; i++)
                        {
                            double diff = _tempIntervalsScratch[i] - avgMs;
                            sqDiffSum += diff * diff;
                        }
                        snap.JitterMs = Math.Sqrt(sqDiffSum / take);
                    }

                    if (_resamplerEnabled)
                    {
                        int nOut = _outIntervalMsBuffer.CopyChronologicalTo(_tempIntervalsScratch);
                        int takeOut = Math.Min(nOut, 40);
                        if (takeOut >= 3)
                        {
                            double sumOut = 0.0;
                            for (int i = nOut - takeOut; i < nOut; i++)
                            {
                                sumOut += _tempIntervalsScratch[i];
                            }
                            double avgOut = sumOut / takeOut;
                            snap.EffectiveOutputHz = (avgOut > 0.1) ? Math.Max(snap.RawHardwareHz, 1000.0 / avgOut) : snap.RawHardwareHz;
                        }
                        else
                        {
                            snap.EffectiveOutputHz = snap.RawHardwareHz;
                        }
                    }
                    else
                    {
                        snap.EffectiveOutputHz = snap.RawHardwareHz;
                    }
                }
            }

            snap.ManagedHeapBytes = GC.GetTotalMemory(false);
            return snap;
        }

        public void Dispose()
        {
            _isDisposed = true;
            if (_timerPeriodActive)
            {
                try { timeEndPeriod(1); _timerPeriodActive = false; } catch { }
            }
            if (_hookHandle != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }
}
