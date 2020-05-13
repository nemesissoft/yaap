﻿using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;

namespace Yaap
{
    /// <summary>
    /// All of the pinvoke stuff below is shamelessly stolen from https://github.com/AArnott/pinvoke
    /// The reason for this IP theft is that the netstandard nuget packages for netstandard DO NOT include
    /// any of the console stuff in to weird decision I don't care to understand
    /// </summary>
    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    internal class Win32Console
    {
        private const string KERNEL32 = "kernel32.dll";
        [DllImport(nameof(KERNEL32), SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetConsoleMode(IntPtr hConsoleHandle, out ConsoleBufferModes lpMode);

        [DllImport(nameof(KERNEL32), SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleMode(IntPtr hConsoleHandle, ConsoleBufferModes dwMode);

        [DllImport(nameof(KERNEL32), SetLastError = true)]
        private static extern IntPtr GetStdHandle(StdHandle nStdHandle);

        [DllImport(nameof(KERNEL32), SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetStdHandle(StdHandle nStdHandle, IntPtr nHandle);

        [DllImport(nameof(KERNEL32), SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleOutputCP(uint wCodePageId);

        [DllImport(nameof(KERNEL32), SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetConsoleCP(uint wCodePageId);

        [DllImport(nameof(KERNEL32), SetLastError = true)]
        private static extern uint GetConsoleOutputCP();

        [DllImport(nameof(KERNEL32), SetLastError = true)]
        private static extern uint GetConsoleCP();

        [DllImport(KERNEL32, SetLastError = true)]
        private static extern bool GetCurrentConsoleFontEx(IntPtr hConsoleOutput, bool bMaximumWindow, [In, Out] ConsoleFontInfoEx lpConsoleCurrentFont);

        [DllImport(KERNEL32, SetLastError = true)]
        private static extern FileType GetFileType(IntPtr hFile);

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        private enum FileType : uint
        {
            FILE_TYPE_CHAR = 0x0002,
            FILE_TYPE_DISK = 0x0001,
            FILE_TYPE_PIPE = 0x0003,
            FILE_TYPE_REMOTE = 0x8000,
            FILE_TYPE_UNKNOWN = 0x0000,
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        internal class ConsoleFontInfoEx
        {
            private readonly int cbSize = Marshal.SizeOf(typeof(ConsoleFontInfoEx));
            internal int FontIndex;
            internal short FontWidth;
            internal short FontHeight;
            internal int FontFamily;
            internal int FontWeight;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            internal string FaceName;
        }


        /// <summary>
        /// Designates the console buffer mode on the <see cref="GetConsoleMode(IntPtr, out ConsoleBufferModes)"/> and <see cref="SetConsoleMode(IntPtr, ConsoleBufferModes)"/> functions
        /// </summary>
        [Flags]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        private enum ConsoleBufferModes
        {
            ENABLE_PROCESSED_INPUT = 0x0001,

            ENABLE_PROCESSED_OUTPUT = 0x0001,

            ENABLE_LINE_INPUT = 0x0002,

            ENABLE_WRAP_AT_EOL_OUTPUT = 0x0002,

            ENABLE_ECHO_INPUT = 0x0004,

            ENABLE_VIRTUAL_TERMINAL_PROCESSING = 0x0004,

            ENABLE_WINDOW_INPUT = 0x0008,

            DISABLE_NEWLINE_AUTO_RETURN = 0x0008,

            ENABLE_MOUSE_INPUT = 0x0010,

            ENABLE_LVB_GRID_WORLDWIDE = 0x0010,

            ENABLE_INSERT_MODE = 0x0020,

            ENABLE_QUICK_EDIT_MODE = 0x0040,

            ENABLE_VIRTUAL_TERMINAL_INPUT = 0x0200,
        }

        /// <summary>
        /// Standard handles for the <see cref="GetStdHandle(StdHandle)"/> and <see cref="SetStdHandle"/> methods.
        /// </summary>
        [Flags]
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "UnusedMember.Global")]
        private enum StdHandle
        {
            /// <summary>
            /// The standard input device. Initially, this is the console input buffer, CONIN$.
            /// </summary>
            STD_INPUT_HANDLE = -10,

            /// <summary>
            /// The standard output device. Initially, this is the active console screen buffer, CONOUT$.
            /// </summary>
            STD_OUTPUT_HANDLE = -11,

            /// <summary>
            /// The standard error device. Initially, this is the active console screen buffer, CONOUT$.
            /// </summary>
            STD_ERROR_HANDLE = -12,
        }

        // ReSharper disable InconsistentNaming
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);
        private static ConsoleBufferModes _originalOutMode, _originalInMode;
        private static uint _originalConsoleOutCP, _originalConsoleCP;
        private static string _consoleFontName;
        // ReSharper restore InconsistentNaming

        internal static string ConsoleFontName
        {
            get
            {
                if (_consoleFontName != null)
                    return _consoleFontName;
                // Set output mode to handle virtual terminal sequences
                var hOut = GetStdHandle(StdHandle.STD_OUTPUT_HANDLE);
                if (hOut == INVALID_HANDLE_VALUE)
                    return _consoleFontName = string.Empty;

                var consoleInfo = new ConsoleFontInfoEx();
                GetCurrentConsoleFontEx(hOut, false, consoleInfo);
                return _consoleFontName = consoleInfo.FaceName;
            }
        }

        /// <summary>
        /// Adapted from:
        /// https://docs.microsoft.com/en-us/windows/console/console-virtual-terminal-sequences
        /// </summary>
        /// <returns></returns>
        internal static bool EnableVt100Stuffs()
        {
            // Set output mode to handle virtual terminal sequences
            var hOut = GetStdHandle(StdHandle.STD_OUTPUT_HANDLE);
            if (hOut == INVALID_HANDLE_VALUE)
                return false;
            var hIn = GetStdHandle(StdHandle.STD_INPUT_HANDLE);
            if (hIn == INVALID_HANDLE_VALUE)
                return false;

            if (!GetConsoleMode(hOut, out _originalOutMode))
                return false;
            if (!GetConsoleMode(hIn, out _originalInMode))
                return false;

            // Apparently this can't fail?
            _originalConsoleOutCP = GetConsoleOutputCP();
            _originalConsoleCP = GetConsoleCP();

            var dwOutMode = _originalOutMode | ConsoleBufferModes.ENABLE_VIRTUAL_TERMINAL_PROCESSING;

            if (!SetConsoleMode(hOut, dwOutMode))
                return false; // Failed to set any VT mode, can't do anything here.

            var dwInMode = _originalInMode | ConsoleBufferModes.ENABLE_VIRTUAL_TERMINAL_INPUT;
            if (!SetConsoleMode(hIn, dwInMode))
                return false; // Failed to set VT input mode, can't do anything here.

            Console.OutputEncoding = Encoding.UTF8;
            const uint CP_UTF8 = 65001;
            SetConsoleOutputCP(CP_UTF8);
            SetConsoleCP(CP_UTF8);
            return true;
        }

        internal static void RestoreTerminalToPristineState()
        {
            // Set output mode to handle virtual terminal sequences
            var hOut = GetStdHandle(StdHandle.STD_OUTPUT_HANDLE);
            if (hOut == INVALID_HANDLE_VALUE)
                return;
            var hIn = GetStdHandle(StdHandle.STD_INPUT_HANDLE);
            if (hIn == INVALID_HANDLE_VALUE)
                return;

            SetConsoleMode(hOut, _originalOutMode);
            SetConsoleMode(hIn, _originalInMode);
            SetConsoleOutputCP(_originalConsoleOutCP);
            SetConsoleCP(_originalConsoleCP);
        }

        public static bool DetectConsoleRedirectionOnWindows() => GetFileType(GetStdHandle(StdHandle.STD_OUTPUT_HANDLE)) != FileType.FILE_TYPE_CHAR;
    }
}
