using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Formatting;
using System.Threading;
using JetBrains.Annotations;

namespace Yaap
{
    internal static class YaapRegistry
    {
        private static Thread _monitorThread;
        private static char[] _chars = new char[Console.WindowWidth * 10];
        private static readonly object _consoleLock = new object();
        private static readonly object _threadLock = new object();
        private static readonly IDictionary<int, Yaap> _instances = new ConcurrentDictionary<int, Yaap>();
        private static int _maxYaapPosition;
        private static int _totalLinesAddedAfterYaaps;
        private static int _isRunning;
        private static bool _wasCursorHidden;
        internal static ThreadLocal<Stack<Yaap>> YaapStack =
            new ThreadLocal<Stack<Yaap>>(() => new Stack<Yaap>());

        private static readonly bool _vt100IsSupported;
        private static readonly bool _isConsoleRedirected;

        static YaapRegistry()
        {
            _isConsoleRedirected = DetectConsoleRedirection();

            if (_isConsoleRedirected)
                return;

            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                OnCancelKeyPress(null, null);
            };

            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                _vt100IsSupported = true;
                return;
            }

            _vt100IsSupported = RedPill();
            AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            {
                BluePill();
            };
        }

        private static bool DetectConsoleRedirection() =>
            Environment.OSVersion.Platform switch
            {
                PlatformID.Win32NT => Win32Console.DetectConsoleRedirectionOnWindows(),
                PlatformID.MacOSX => DetectConsoleRedirectionOnPosix(),
                PlatformID.Unix => DetectConsoleRedirectionOnPosix(),
                _ => false
            };

        private static bool DetectConsoleRedirectionOnPosix() =>
            !Mono.Unix.Native.Syscall.isatty(1);

        private static bool IsRunning
        {
            get => _isRunning == 1;
            set => Interlocked.Exchange(ref _isRunning, value ? 1 : 0);
        }

        internal static void AddInstance(Yaap yaap)
        {
            lock (_threadLock)
            {
                lock (_consoleLock)
                {
                    _instances.Add(GetOrSetVerticalPosition(yaap), yaap);

                    if (IsRunning)
                    {
                        // Windows console (in the case we are on windows) has already been red-pilled
                        // So repaint() and bye-bye
                        RepaintYaap(yaap);
                        return;
                    }

                    // If we are just starting up the monitoring thread, we've
                    // just potentially red-pilled the windows console, so we can repaint now
                    RepaintYaap(yaap);

                    IsRunning = true;
                    Console.CancelKeyPress += OnCancelKeyPress;
                }

                if (_isConsoleRedirected)
                    return;
                _monitorThread = _vt100IsSupported ? new Thread(UpdateYaapsOnVt100) : new Thread(UpdateYaapsOnWindowsNoVt100);
                _monitorThread.Name = "yaap-updater";
                _monitorThread.Start();
            }
        }

        internal static void RemoveInstance(Yaap yaap)
        {
            lock (_threadLock)
            {
                lock (_consoleLock)
                {
                    // Repaint just before removing for cosmetic purposes:
                    // In case we didn't have a recent update to the progress bar, it might be @ 100%
                    // "in reality" but not visually.... This call will close that gap
                    if (yaap.Settings.Leave)
                    {
                        RepaintYaap(yaap);
                    }
                    else
                    {
                        ClearYaap(yaap);
                    }

                    _instances.Remove(yaap.Position);
                    // Unfortunately, we need to mark that we've drawn
                    // this Yaap for the last time while still holding the console lock...
                    yaap.IsDisposed = true;

                    if (_instances.Count > 0)
                        return;

                    IsRunning = false;
                    Console.CancelKeyPress -= OnCancelKeyPress;

                    if (yaap.Position + 1 == _maxYaapPosition)
                        _maxYaapPosition = _instances.Count == 0 ? 0 : (_instances.Keys.Max() + 1);

                    _totalLinesAddedAfterYaaps = 0;
                }
                _monitorThread.Join();
            }
        }

        private static bool RedPill() => Win32Console.EnableVt100Stuffs();
        private static void BluePill() => Win32Console.RestoreTerminalToPristineState();

        private static int GetOrSetVerticalPosition(Yaap yaap)
        {
            return yaap.Settings.Positioning switch
            {
                YaapPositioning.FlowAndSnapToTop => FlowAndSnapToTop(),
                YaapPositioning.ClearAndAlignToTop => ClearAndAlignToTop(),
                YaapPositioning.FixToBottom => FixToBottom(),
                _ => throw new ArgumentOutOfRangeException()
            };

            int FlowAndSnapToTop()
            {
                if (yaap.Settings.VerticalPosition.HasValue)
                    yaap.Position = yaap.Settings.VerticalPosition.Value;
                else
                {
                    int lastPos = -1;
                    foreach (var p in _instances.Keys)
                    {
                        if (p > lastPos + 1)
                            return yaap.Position = lastPos + 1;
                        lastPos = p;
                    }

                    yaap.Position = ++lastPos;
                }

                if (_maxYaapPosition > yaap.Position)
                    return yaap.Position;

                // This progress bar is taking up one more line
                // than we previously accounted for, so bump the total line count + \n
                for (var l = _maxYaapPosition; l < yaap.Position + 1; l++)
                    Console.WriteLine();
                _maxYaapPosition = yaap.Position + 1;
                return yaap.Position;
            }

            int ClearAndAlignToTop()
            {
                AnsiCodes.SetScrollableRegion(1, Console.WindowHeight);
                return yaap.Position = 0;
            }

            int FixToBottom()
            {
                AnsiCodes.SetScrollableRegion(0, Console.WindowHeight - 1);
                return yaap.Position = Console.WindowHeight;
            }
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            lock (_consoleLock)
            {
                Console.Write("\r");
                Console.Write(AnsiCodes.ERASE_ENTIRE_LINE);
                if (_wasCursorHidden)
                {
                    Console.CursorVisible = true;
                }
                AnsiCodes.SetScrollableRegion(0, Console.BufferHeight + 1);
            }
        }

        private static readonly StringBuffer _buffer = new StringBuffer(Console.WindowWidth * 10);

        private const int INTERVAL_MS = 50;

        private static void UpdateYaapsOnVt100()
        {

            while (IsRunning)
            {
                _buffer.Clear();
                foreach (var y in _instances.Values)
                {
                    if (!y.NeedsRepaint)
                    {
                        continue;
                    }

                    _buffer.Append(AnsiCodes.SAVE_CURSOR_POSITION);
                    AppendYaapToBuffer(y, _buffer);
                }

                if (_buffer.Count > 0)
                {
                    _buffer.Append(AnsiCodes.RESTORE_CURSOR_POSITION);
                    SpillBuffer();
                }

                Thread.Sleep(INTERVAL_MS);
            }
        }

        private static void SpillBuffer()
        {
            if (_buffer.Count > _chars.Length)
                Array.Resize(ref _chars, _buffer.Count);
            _buffer.CopyTo(0, _chars, 0, _buffer.Count);
            Console.Write(_chars, 0, _buffer.Count);
        }

        private static void RepaintYaapWithVt100(Yaap yaap)
        {
            _buffer.Clear();
            _buffer.Append(AnsiCodes.SAVE_CURSOR_POSITION);
            AppendYaapToBuffer(yaap, _buffer);
            _buffer.Append(AnsiCodes.RESTORE_CURSOR_POSITION);
            SpillBuffer();
        }


        private static void AppendYaapToBuffer(Yaap yaap, StringBuffer buffer)
        {
            buffer.AppendFormat(AnsiCodes.CSI + "{0}d", yaap.Position + 1);
            buffer.Append('\r');
            buffer.Append(AnsiCodes.ERASE_TO_LINE_END);

            yaap.Repaint(buffer);
        }

        private static void UpdateYaapsOnWindowsNoVt100()
        {
            bool lockWasTaken = false;
            try
            {
                _wasCursorHidden = false;
                while (IsRunning)
                {
                    foreach (var y in _instances.Values)
                    {
                        if (!y.NeedsRepaint)
                        {
                            continue;
                        }

                        if (!lockWasTaken)
                            Monitor.Enter(_consoleLock, ref lockWasTaken);

                        if (y.Settings.DisableCursorDuringUpdates && !_wasCursorHidden)
                        {
                            Console.CursorVisible = false;
                            _wasCursorHidden = true;
                        }

                        RepaintYaapWindowsNoVt100(y);
                    }


                    if (_wasCursorHidden)
                    {
                        Console.CursorVisible = true;
                        _wasCursorHidden = false;
                    }

                    if (lockWasTaken)
                    {
                        lockWasTaken = false;
                        Monitor.Exit(_consoleLock);
                    }

                    Thread.Sleep(INTERVAL_MS);
                }
            }
            finally
            {
                if (lockWasTaken)
                    Monitor.Exit(_consoleLock);
            }
        }

        private static void RepaintYaapWindowsNoVt100(Yaap yaap)
        {
            _buffer.Clear();
            var (x, y) = MoveTo(yaap);
            _buffer.Append('\r');
            _buffer.Append(AnsiCodes.ERASE_TO_LINE_END);
            yaap.Repaint(_buffer);
            SpillBuffer();
            MoveTo(x, y);
        }


        private static void RepaintYaap(Yaap yaap)
        {
            if (_vt100IsSupported)
            {
                RepaintYaapWithVt100(yaap);
            }
            else
            {
                RepaintYaapWindowsNoVt100(yaap);
            }
        }

        private static void ClearYaap(Yaap yaap)
        {
            if (_vt100IsSupported)
            {
                ClearYaapWithVt100(yaap);
            }
            else
            {
                ClearYaapWindowsNoVt100(yaap);
            }
        }

        private static void ClearYaapWindowsNoVt100(Yaap yaap)
        {
            lock (_consoleLock)
            {
                var (x, y) = MoveTo(yaap);
                yaap.Repaint(_buffer);
                // Looks silly eh?
                // The reason is we don't want to bother to understand how many printable characters are in the buffer
                // so we simply backspace _buffer.Count and we know for sure we've deleted the entire line
                // without bothering to decode VT100
                _buffer.Append('\b', _buffer.Count);
                SpillBuffer();
                MoveTo(x, y);
            }
        }

        private static void ClearYaapWithVt100(Yaap yaap)
        {
            _buffer.Append(AnsiCodes.SAVE_CURSOR_POSITION);
            _buffer.AppendFormat(AnsiCodes.CSI + "{0}d", yaap.Position + 1);
            _buffer.Append(AnsiCodes.ERASE_ENTIRE_LINE);
            _buffer.Append(AnsiCodes.RESTORE_CURSOR_POSITION);
            SpillBuffer();
        }

        internal static void ClearScreen()
        {
            lock (_consoleLock)
            {
                Console.Write(AnsiCodes.CLEAR_SCREEN);
            }
        }

        private static void MoveTo(int x, int y) => Console.SetCursorPosition(x, y);

        private static (int x, int y) MoveTo(Yaap yaap)
        {
            var (x, y) = ConsolePosition;
            switch (yaap.Settings.Positioning)
            {
                case YaapPositioning.FlowAndSnapToTop:
                    Console.CursorTop = Math.Max(0, y - (_maxYaapPosition - yaap.Position + _totalLinesAddedAfterYaaps));
                    break;
                case YaapPositioning.ClearAndAlignToTop:
                case YaapPositioning.FixToBottom:
                    Console.CursorTop = yaap.Position;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
            return (x, y);

        }

        private static (int x, int y) ConsolePosition => (Console.CursorLeft, Console.CursorTop);

        internal static void Write(string s) { lock (_consoleLock) { Console.Write(s); } }

        internal static void WriteLine(string s)
        {
            lock (_consoleLock)
            {
                Console.WriteLine(s);
                if (_maxYaapPosition > 0)
                    _totalLinesAddedAfterYaaps++;
            }
        }

        internal static void WriteLine()
        {
            lock (_consoleLock)
            {
                Console.WriteLine();
                if (_maxYaapPosition > 0)
                    _totalLinesAddedAfterYaaps++;
            }
        }

    }

    /// <inheritdoc />
    /// <summary>
    /// Represents a text mode progress bar control, that can visually provide user feedback as to the progress
    /// a long-standing operation, including progress visualization, elapsed time, total time, rate and more
    /// </summary>
    public class Yaap : IDisposable
    {
        private const double TOLERANCE = 1e-6;

        private static bool _unicodeNotWorking;
        private static readonly char[] _asciiBarStyle = { '#' };
        private readonly char[] _selectedBarStyle;
        private double _nextRepaintProgress;
        private readonly string _progressCountFmt;
        private readonly int _maxGlyphWidth;
        private readonly double _repaintProgressIncrement;
        /// <summary>
        /// Measures time of operation
        /// </summary>
        protected readonly Stopwatch Sw;
        private TimeSpan _totalTime;
        private static readonly long _swTicksIn1Hour = Stopwatch.Frequency * 3600;
        private long _lastRepaintTicks;
        private double _rate;
        private ulong _lastProgress;
        private readonly string _unitName;
        private readonly string _description;
        private readonly bool _useMetricAbbreviations;
        private readonly double _smoothingFactor;

        static Yaap()
        {
            static void DoUnspeakableThingsOnWindows()
            {
                // ReSharper disable once HeapView.ObjectAllocation.Evident
                // ReSharper disable StringLiteralTypo
                var acceptableUnicodeFonts = new[] {
                    "Hack",
                    "InputMono",
                    "Hasklig",
                    "DejaVu Sans Mono",
                    "Iosevka",
                }; // ReSharper restore StringLiteralTypo

                var vt100IsGo = Win32Console.EnableVt100Stuffs();
                _unicodeNotWorking = vt100IsGo &&
                    acceptableUnicodeFonts.FirstOrDefault(s => Win32Console.ConsoleFontName.StartsWith(s, StringComparison.InvariantCulture)) == null;
            }

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                DoUnspeakableThingsOnWindows();
            }
            else
            {
                _unicodeNotWorking = !(Console.OutputEncoding is UTF8Encoding);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Yaap.Yaap"/> class.
        /// </summary>
        /// <param name="total">The (optional)total number of elements of the wrapped <see cref="IEnumerable{T}"/> that will be enumerated</param>
        /// <param name="initialProgress">The (optional) initial progress value</param>
        /// <param name="settings">The (optional) visual settings overrides</param>
        [PublicAPI]
        public Yaap(ulong total, ulong initialProgress = 0, YaapSettings settings = null)
        {
            Settings = settings ?? new YaapSettings();
            Total = total;
            Progress = initialProgress;

            _unitName = Settings.UnitName;
            _description = Settings.Description;
            _useMetricAbbreviations = Settings.MetricAbbreviations;
            _smoothingFactor = Settings.SmoothingFactor;

            _selectedBarStyle = Settings.UseASCII || _unicodeNotWorking
                ? _asciiBarStyle
                : YaapBarStyleCache.Glyphs[(int)Settings.Style];

            int epilogueLen;
            if (Settings.MetricAbbreviations)
            {
                var (abbrevTotal, suffix) = GetMetricAbbreviation(total);
                _progressCountFmt = $"{{0,3}}{{1}}/{abbrevTotal}{suffix}";
                epilogueLen = "|123K/999K".Length;
            }
            else
            {
                var totalChars = CountDigits(Total);
                _progressCountFmt = $"{{0,{totalChars}}}/{total}";
                epilogueLen = 1 + totalChars * 2 + 1;
            }

            //_timeFmt = $"[{{0}}<{{1}}, {{2}}{unitName}/s]";
            const string EPILOGUE_SAMPLE = " [11:22s<33:44s, 123.45/s]";

            epilogueLen += EPILOGUE_SAMPLE.Length + Settings.UnitName.Length;

            var capturedWidth = Console.WindowWidth - 2;
            if (Settings.Width.HasValue && Settings.Width.Value < capturedWidth)
            {
                capturedWidth = Settings.Width.Value;
            }

            var prologueCount = (string.IsNullOrWhiteSpace(Settings.Description) ? 0 : Settings.Description.Length) + 7;

            _maxGlyphWidth = capturedWidth - prologueCount - epilogueLen;

            _repaintProgressIncrement = (double)Total / (_maxGlyphWidth * _selectedBarStyle.Length);
            if (Math.Abs(_repaintProgressIncrement) < TOLERANCE)
            {
                _repaintProgressIncrement = 1;
            }

            _nextRepaintProgress =
                Progress / _repaintProgressIncrement * _repaintProgressIncrement +
                _repaintProgressIncrement;

            _rate = double.NaN;
            _totalTime = TimeSpan.MaxValue;
            Sw = Stopwatch.StartNew();

            Parent = YaapRegistry.YaapStack.Value.Count == 0
                ? null : YaapRegistry.YaapStack.Value.Peek();

            YaapRegistry.AddInstance(this);

            if (Parent != null)
            {
                Parent.Child = this;
            }

            YaapRegistry.YaapStack.Value.Push(this);

            static int CountDigits(ulong number)
            {
                var digits = 0;
                while (number != 0)
                {
                    number /= 10;
                    digits++;
                }
                return digits;
            }
        }


        private Yaap Parent { get; }

        private Yaap Child { get; set; }

        /// <summary>
        /// The current progress value of the progress bar
        /// <remarks>Always between 0 .. <see cref="Total"/></remarks>
        /// </summary>
        [PublicAPI]
        public ulong Progress { get; set; }

        private double NestedProgress =>
            Child == null ?
                0 :
                (Child.Progress + Child.NestedProgress) / Child.Total;

        /// <summary>
        /// The maximal value of the progress bar which represents 100%
        /// <remarks>When the value is not supplied, only basic statistics will be displayed</remarks>
        /// </summary>
        [PublicAPI]
        public ulong Total { get; }

        /// <summary>
        /// Whether to disable the entire progressbar display
        /// </summary>
        [PublicAPI]
        public bool Disable { get; set; }

        /// <summary>
        /// The vertical position of this instance in relation to other concurrently "live" <see cref="Yaap"/> objects
        /// </summary>
        [PublicAPI]
        public int Position { get; internal set; }

        /// <summary>
        /// The visual settings used for this instance
        /// </summary>
        /// <value>The settings.</value>
        [PublicAPI]
        public YaapSettings Settings { get; }

        /// <summary>
        /// The elapsed amount of time this operation has taken so far
        /// </summary>
        /// <value>The elapsed time.</value>
        [PublicAPI]
        public TimeSpan ElapsedTime { get; private set; }

        /// <summary>
        /// The predicted total amount of time this operation will take
        /// </summary>
        /// <value>The total time.</value>
        [PublicAPI]
        public TimeSpan TotalTime { get; private set; }


        private int _forceRepaint;

        private bool ForceRepaint
        {
            get => _forceRepaint == 1;
            set => Interlocked.Exchange(ref _forceRepaint, value ? 1 : 0);
        }


        /// <summary>
        /// The current <see cref="YaapState"/> of the instance
        /// </summary>
        /// <value>The state.</value>
        [PublicAPI]
        public YaapState State
        {
            get => _state;
            set
            {
                if (_state == value)
                {
                    return;
                }

                _state = value;
                ForceRepaint = true;
            }
        }

        private static readonly string[] _metricUnits = { "", "k", "M", "G", "T", "P", "E", "Z" };
        private TerminalColor _lastColor;
        private YaapState _state;

        private static (ulong num, string abbrev) GetMetricAbbreviation(ulong num)
        {
            for (var i = 0; i < _metricUnits.Length; i++)
            {
                if (num < 1000)
                    return (num, _metricUnits[i]);
                num /= 1000;
            }
            throw new ArgumentOutOfRangeException(nameof(num), "is too large");
        }

        private static (double num, string abbrev) GetMetricAbbreviation(double num)
        {
            for (var i = 0; i < _metricUnits.Length; i++)
            {
                if (num < 1000)
                    return (num, _metricUnits[i]);
                num /= 1000;
            }
            throw new ArgumentOutOfRangeException(nameof(num), "is too large");
        }

        internal bool NeedsRepaint
        {
            get
            {
                var updateSpan = Stopwatch.Frequency;
                var swElapsedTicks = Sw.ElapsedTicks;

                if (swElapsedTicks >= _swTicksIn1Hour || _totalTime.Ticks >= TimeSpan.TicksPerHour)
                    updateSpan = Stopwatch.Frequency * 60;

                if (ForceRepaint)
                {
                    ForceRepaint = false;
                    return true;
                }

                if (_lastRepaintTicks + updateSpan < swElapsedTicks)
                    return true;

                if ((Progress + NestedProgress) >= _nextRepaintProgress)
                {
                    _nextRepaintProgress += _repaintProgressIncrement;
                    return true;
                }

                return false;
            }
        }

        private int _isDisposed;

        internal bool IsDisposed
        {
            get => _isDisposed == 1;
            set => Interlocked.Exchange(ref _isDisposed, value ? 1 : 0);
        }

        /// <summary>
        /// Releases all resources used by the progress bar
        /// </summary>
        public void Dispose()
        {
            YaapRegistry.YaapStack.Value.Pop();
            YaapRegistry.RemoveInstance(this);
        }


        private bool ShouldShoveDescription => (Settings.Elements & YaapElement.Description) != 0;
        private bool ShouldShoveProgressPercent => (Settings.Elements & YaapElement.ProgressPercent) != 0;
        private bool ShouldShoveProgressBar => (Settings.Elements & YaapElement.ProgressBar) != 0;
        private bool ShouldShoveProgressCount => (Settings.Elements & YaapElement.ProgressCount) != 0;
        private bool ShouldShoveTime => (Settings.Elements & YaapElement.Time) != 0;
        private bool ShouldShoveRate => (Settings.Elements & YaapElement.Rate) != 0;

        internal void Repaint(StringBuffer buffer)
        {
            // Capture progress while repainting
            var progress = Progress;
            var nestedProgress = NestedProgress;
            var elapsedTicks = Sw.ElapsedTicks;

            (_rate, _totalTime) = RecalculateRateAndTotalTime();

            var cs = Settings.ColorScheme;


            if (ShouldShoveDescription)
            {
                ShoveDescription();
            }

            if (ShouldShoveProgressPercent)
            {
                ShoveProgressPercentage();
            }

            if (ShouldShoveProgressBar)
            {
                ShoveProgressBar();
            }

            if (ShouldShoveProgressCount)
            {
                ShoveProgressTotals();
            }

            buffer.Append(' ');
            //[{{0}}<{{1}}, {{2}}{unitName}/s]

            // At least one of Time|Rate is turned on?
            if ((Settings.Elements & (YaapElement.Rate | YaapElement.Time)) != 0)
            {
                buffer.Append('[');
            }

            ShoveTime();
            if (ShouldShoveTime)
            {
                buffer.Append(", ");
            }

            if (ShouldShoveRate)
            {
                ShoveRate();
            }

            if ((Settings.Elements & (YaapElement.Rate | YaapElement.Time)) != 0)
            {
                buffer.Append(']');
            }

            buffer.Append(AnsiCodes.ERASE_TO_LINE_END);

            _lastProgress = progress;
            _lastRepaintTicks = elapsedTicks;

            (double rate, TimeSpan totalTime) RecalculateRateAndTotalTime()
            {
                // If we're "told" not to smooth out the rate/total time prediction,
                // we just use the whole thing for the progress calc, otherwise we continuously sample
                // the last rate update since the previous rate and smooth it out using EMA/SmoothingFactor
                double rate;
                if (Math.Abs(_smoothingFactor) < TOLERANCE)
                {
                    rate = ((double)progress * Stopwatch.Frequency) / elapsedTicks;
                }
                else
                {
                    var dProgress = progress - _lastProgress;
                    var dTicks = elapsedTicks - _lastRepaintTicks;

                    var lastRate = ((double)dProgress * Stopwatch.Frequency) / dTicks;
                    rate = _lastRepaintTicks == 0 ? lastRate : _smoothingFactor * lastRate + (1 - _smoothingFactor) * _rate;
                }

                var totalTime = rate <= 0 ? TimeSpan.MaxValue : new TimeSpan((long)(Total * TimeSpan.TicksPerSecond / rate));
                // In case rate is so slow, we are overflowing
                if (totalTime.Ticks < 0)
                    totalTime = TimeSpan.MaxValue;
                return (rate, totalTime);
            }


            void ShoveDescription()
            {
                if (string.IsNullOrWhiteSpace(_description))
                {
                    return;
                }
                buffer.Append(_description);
                buffer.Append(": ");
            }

            void ShoveProgressPercentage()
            {
                ChangeColor(cs.ProgressPercentColor);
                buffer.AppendFormat("{0,3}%", (int)(((progress + nestedProgress) / Total) * 100));
                ResetColor();
            }

            void ShoveProgressBar()
            {
                buffer.Append('|');

                ChangeColor(SelectProgressBarColor());
                var numChars = _selectedBarStyle.Length > 1
                    ? RenderComplexProgressGlyphs()
                    : RenderSimpleProgressGlyphs();
                ResetColor();
                var numSpaces = _maxGlyphWidth - numChars;
                if (numSpaces > 0)
                    buffer.Append(' ', numSpaces);
                buffer.Append('|');

                TerminalColor SelectProgressBarColor() =>
                    State switch
                    {
                        YaapState.Running => Settings.ColorScheme.ProgressBarColor,
                        YaapState.Paused => Settings.ColorScheme.ProgressBarPausedColor,
                        YaapState.Stalled => Settings.ColorScheme.ProgressBarStalledColor,
                        _ => throw new ArgumentOutOfRangeException()
                    };

                int RenderSimpleProgressGlyphs()
                {
                    var numGlyphChars = (int)(progress * (ulong)_maxGlyphWidth / Total);
                    buffer.Append(_selectedBarStyle[0], numGlyphChars);
                    return numGlyphChars;
                }

                int RenderComplexProgressGlyphs()
                {
                    var blocks = ((progress + nestedProgress) * (_maxGlyphWidth * _selectedBarStyle.Length)) / Total;
                    Debug.Assert(blocks >= 0);
                    var completeBlocks = (int)(blocks / _selectedBarStyle.Length);
                    buffer.Append(_selectedBarStyle[_selectedBarStyle.Length - 1], completeBlocks);
                    var lastCharIdx = (int)(blocks % _selectedBarStyle.Length);

                    if (lastCharIdx == 0)
                    {
                        return completeBlocks;
                    }

                    buffer.Append(_selectedBarStyle[lastCharIdx]);
                    return completeBlocks + 1;
                }

            }

            void ShoveProgressTotals()
            {
                ChangeColor(cs.ProgressCountColor);
                if (_useMetricAbbreviations)
                {
                    var (abbrevNum, suffix) = GetMetricAbbreviation(Progress);
                    buffer.AppendFormat(_progressCountFmt, abbrevNum, suffix);
                }
                else
                {
                    buffer.AppendFormat(_progressCountFmt, Progress);
                }

                ResetColor();
            }


            void ShoveTime()
            {
                ChangeColor(cs.TimeColor);
                WriteTimes(buffer, new TimeSpan((elapsedTicks * TimeSpan.TicksPerSecond) / Stopwatch.Frequency), _totalTime);
                ResetColor();
            }

            void ShoveRate()
            {
                ChangeColor(cs.RateColor);
                if (_useMetricAbbreviations)
                {
                    var (abbrevNum, suffix) = GetMetricAbbreviation(_rate);
                    if (abbrevNum < 100)
                        buffer.AppendFormat("{0:F2}{1}{2}/s", abbrevNum, suffix, _unitName);
                    else
                        buffer.AppendFormat("{0}{1}{2}/s", (int)abbrevNum, suffix, _unitName);
                }
                else
                {
                    if (_rate < 100)
                        buffer.AppendFormat("{0:F2}{1}/s", _rate, _unitName);
                    else
                        buffer.AppendFormat("{0}{1}/s", (int)_rate, _unitName);
                }
                ResetColor();
            }

            void ChangeColor(TerminalColor color)
            {
                _lastColor = color;
                buffer.Append(color.EscapeCode);
            }

            void ResetColor()
            {
                if (_lastColor == TerminalColor.None)
                    return;
                buffer.Append(TerminalColor.Reset.EscapeCode);
            }
        }


        private static void WriteTimes(StringBuffer buffer, TimeSpan elapsed, TimeSpan remaining)
        {
            Debug.Assert(elapsed.Ticks >= 0);
            Debug.Assert(remaining.Ticks >= 0);
            var (eDays, eHours, eMinutes, eSeconds, _) = elapsed;
            var (rDays, rHours, rMinutes, rSeconds, _) = remaining;

            if (eDays + rDays > 0)
            {
                // Print days formatting
            }
            else if (eHours + rHours > 0)
            {
                if (elapsed == TimeSpan.MaxValue)
                    buffer.Append("--:--?<");
                else
                    buffer.AppendFormat("{0:D2}:{1:D2}m<", eHours, eMinutes);
                if (remaining == TimeSpan.MaxValue)
                    buffer.Append("--:--?");
                else
                    buffer.AppendFormat("{0:D2}:{1:D2}m", rHours, rMinutes);
            }
            else
            {
                if (elapsed == TimeSpan.MaxValue)
                {
                    buffer.Append("--:--?<");
                }
                else
                {
                    buffer.AppendFormat("{0:D2}:{1:D2}s<", eMinutes, eSeconds);
                }

                if (remaining == TimeSpan.MaxValue)
                {
                    buffer.Append("--:--?");
                }
                else
                {
                    buffer.AppendFormat("{0:D2}:{1:D2}s", rMinutes, rSeconds);
                }
            }
        }
    }

    /// <summary>
    /// Represents a Yaap wrapped <see cref="IEnumerable{T}"/> object, where the enumerator progress automatically changes
    /// the Yaap visual representation without further need to manually update the progress state
    /// </summary>
    /// <typeparam name="T">The type of objects to enumerate</typeparam>
    public class YaapEnumerable<T> : Yaap, IEnumerable<T>
    {
        private readonly IEnumerable<T> _enumerable;
        private static Func<IEnumerable<T>, int> _cheapCount;

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        internal YaapEnumerable(IEnumerable<T> e, ulong? total = null, ulong initialProgress = 0, YaapSettings settings = null) :
            base(total ?? (ulong)GetCheapCount(e), initialProgress, settings) =>
            _enumerable = e;

        /// <summary>
        /// Attempt to get a "cheap" count value for the <see cref="IEnumerable{T}"/>, where "cheap" means that the enumerable is
        /// never consumed no matter what
        /// </summary>
        /// <param name="source">The <see cref="IEnumerable{T}"/> object</param>
        /// <returns>The count value, or -1 in case the cheap count failed</returns>
        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        public static int GetCheapCount(IEnumerable<T> source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            return source switch
            {
                ICollection<T> collectionOfT => collectionOfT.Count,
                ICollection collection => collection.Count,
                _ => CheapCountDelegate(source)
            };
        }

        #region Avert Your Eyes!

        [SuppressMessage("ReSharper", "IdentifierTypo")]
        private static Func<IEnumerable<T>, int> CheapCountDelegate
        {
            get
            {
                return _cheapCount ??= GenerateGetCount();

                static Func<IEnumerable<T>, int> GenerateGetCount()
                {
                    var iilp = typeof(Enumerable).Assembly.GetType("System.Linq.IIListProvider`1");
                    Debug.Assert(iilp != null);
                    var iilpt = iilp.MakeGenericType(typeof(T));
                    Debug.Assert(iilpt != null);
                    var getCountMi = iilpt.GetMethod("GetCount", BindingFlags.Public | BindingFlags.Instance, null,
                        new[] { typeof(bool) }, null);
                    Debug.Assert(getCountMi != null);
                    var param = Expression.Parameter(typeof(IEnumerable<T>));

                    var castAndCall = Expression.Call(Expression.Convert(param, iilpt), getCountMi,
                        Expression.Constant(true));

                    var body = Expression.Condition(Expression.TypeIs(param, iilpt), castAndCall,
                        Expression.Constant(-1));

                    return Expression.Lambda<Func<IEnumerable<T>, int>>(body, param).Compile();
                }
            }
        }
        #endregion

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <returns>Returns an enumerator that iterates through the collection.</returns>
        public IEnumerator<T> GetEnumerator()
        {
            // In the case of enumerable we can actually start ticking the elapsed clock
            // at a later, more precise, time, so lets do it...
            Sw.Restart();
            foreach (var t in _enumerable)
            {
                yield return t;
                Progress++;
            }
            Dispose();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>
    /// A static extension class that provides <see cref="IEnumerable{T}"/> <see cref="YaapEnumerable{T}"/> wrappers
    /// </summary>
    public static class YaapEnumerableExtensions
    {
        /// <summary>
        /// Wrap the provided <see cref="IEnumerable{T}"/> with a <see cref="YaapEnumerable{T}"/> object
        /// </summary>
        /// <typeparam name="T">The type of objects to enumerate</typeparam>
        /// <param name="e">The <see cref="IEnumerable{T}"/> instance to wrap</param>
        /// <param name="total">The (optional)total number of elements of the wrapped <see cref="IEnumerable{T}"/> that will be enumerated</param>
        /// <param name="initialProgress">The (optional) initial progress value</param>
        /// <param name="settings">The (optional) visual settings overrides</param>
        /// <returns>The newly instantiated <see cref="YaapEnumerable{T}"/> wrapping the provided <see cref="IEnumerable{T}"/></returns>
        public static YaapEnumerable<T> Yaap<T>(this IEnumerable<T> e, ulong? total = null, ulong initialProgress = 0,
            YaapSettings settings = null) =>
            new YaapEnumerable<T>(e, total, initialProgress, settings);
    }

    internal static class DateTimeDeconstruction
    {
        //private const long TICKS_PER_MICRO_SECONDS = 10;
        private const long TICKS_PER_MILLISECOND = 10_000;
        private const long TICKS_PER_SECOND = TICKS_PER_MILLISECOND * 1_000;
        private const long TICKS_PER_MINUTE = TICKS_PER_SECOND * 60;
        private const long TICKS_PER_HOUR = TICKS_PER_MINUTE * 60;
        private const long TICKS_PER_DAY = TICKS_PER_HOUR * 24;

        public static void Deconstruct(this TimeSpan timeSpan, out int days, out int hours, out int minutes, out int seconds, out int ticks)
        {
            if (timeSpan == TimeSpan.MaxValue)
            {
                days = hours = minutes = seconds = ticks = 0;
                return;
            }
            long t = timeSpan.Ticks;
            days = (int)(t / TICKS_PER_DAY);
            hours = (int)((t / TICKS_PER_HOUR) % 24);
            minutes = (int)((t / TICKS_PER_MINUTE) % 60);
            seconds = (int)((t / TICKS_PER_SECOND) % 60);
            ticks = (int)(t % 10_000_000);
        }
    }
}
