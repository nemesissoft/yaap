using System;
using System.Drawing;

namespace Yaap
{
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

    internal static class ANSICodes
    {
        public const string ESC = "\u001B";
        public const string ResetTerminal = ESC + "c";
        public const string CSI = ESC + "[";
        public const string ClearScreen = CSI + "2J";
        public const string EraseToLineEnd = CSI + "K";
        public const string EraseEntireLine = CSI + "2K";
        public const string EraseToLineStart = CSI + "1K";
        public static string SaveCursorPosition = ESC + "7";
        public static string RestoreCursorPosition = ESC + "8";


        internal static void SetScrollableRegion(int top, int bottom) =>
            Console.Write($"{CSI}{top};{bottom}r");
    }
}
