using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using JetBrains.Annotations;

namespace Yaap
{
    [UsedImplicitly]
    internal static class ColorDeconstruction
    {
        public static void Deconstruct(this Color color, out int r, out int g, out int b, out int a)
        {
            r = color.R;
            g = color.G;
            b = color.B;
            a = color.A;
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    internal static class AnsiCodes
    {
        public const string ESC = "\u001B";
        public const string RESET_TERMINAL = ESC + "c";
        public const string CSI = ESC + "[";
        public const string CLEAR_SCREEN = CSI + "2J";
        public const string ERASE_TO_LINE_END = CSI + "K";
        public const string ERASE_ENTIRE_LINE = CSI + "2K";
        public const string ERASE_TO_LINE_START = CSI + "1K";
        public const string SAVE_CURSOR_POSITION = ESC + "7";
        public const string RESTORE_CURSOR_POSITION = ESC + "8";
        public const string FG_RESET = CSI + "0m";

        internal static void SetScrollableRegion(int top, int bottom) =>
            Console.Write($"{CSI}{top};{bottom}r");
    }
}
