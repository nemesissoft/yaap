using System;
using System.ComponentModel;
using JetBrains.Annotations;

namespace Yaap
{
    internal static class YaapBarStyleCache
    {
        private static readonly char[][] _glyphs = {
            new[] {'▏', '▎', '▍', '▌', '▋', '▊', '▉', '█'},
            new[] {'▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'},
            new[] {'⣀', '⣄', '⣤', '⣦', '⣶', '⣷', '⣿'},
            new[] {'⣀', '⣄', '⣆', '⣇', '⣧', '⣷', '⣿'},
            new[] {'○', '◔', '◐', '◕', '⬤'},
            new[] {'□', '◱', '◧', '▣', '■'},
            new[] {'□', '◱', '▨', '▩', '■'},
            new[] {'□', '◱', '▥', '▦', '■'},
            new[] {'⬜', '⬛'},
            new[] {'░', '▒', '▓', '█'},
            new[] {'░', '█'},
            new[] {'▱', '▰'},
            new[] {'▭', '◼'},
            new[] {'▯', '▮'},
            new[] {'◯', '⬤'},
            new[] {'⚪', '⚫'},
        };

        internal static char[] GetBar(YaapBarStyle style)
        {
            if (!Enum.IsDefined(typeof(YaapBarStyle), style))
                throw new InvalidEnumArgumentException(nameof(style), (int)style, typeof(YaapBarStyle));

            return _glyphs[(int) style];
        }
    }

    /// <summary>
    /// An enumeration representing the various visual styles of a Yaap progress bar component
    /// </summary>
    [PublicAPI]
    public enum YaapBarStyle : byte
    {
        /// <summary>
        /// '▏', '▎', '▍', '▌', '▋', '▊', '▉', '█'
        /// </summary>
        BarHorizontal,

        /// <summary>
        /// '▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'
        /// </summary>
        BarVertical,

        /// <summary>
        /// '⣀', '⣄', '⣤', '⣦', '⣶', '⣷', '⣿'
        /// </summary>
        DotsHorizontal,

        /// <summary>
        /// '⣀', '⣄', '⣆', '⣇', '⣧', '⣷', '⣿'
        /// </summary>
        DotsVertical,

        /// <summary>
        /// '○', '◔', '◐', '◕', '⬤'
        /// </summary>
        Clock,

        /// <summary>
        /// '□', '◱', '◧', '▣', '■'
        /// </summary>
        Squares1,

        /// <summary>
        /// '□', '◱', '▨', '▩', '■'
        /// </summary>
        Squares2,

        /// <summary>
        /// '□', '◱', '▥', '▦', '■'
        /// </summary>
        Squares3,

        /// <summary>
        /// '⬜', '⬛'
        /// </summary>
        ShortSquares,

        /// <summary>
        /// '░', '▒', '▓', '█'
        /// </summary>
        LongMesh,

        /// <summary>
        /// '░', '█'
        /// </summary>
        ShortMesh,

        /// <summary>
        /// '▱', '▰'
        /// </summary>
        Parallelogram,

        /// <summary>
        /// '▭', '◼'
        /// </summary>
        Rectangles1,

        /// <summary>
        /// '▯', '▮'
        /// </summary>
        Rectangles2,

        /// <summary>
        /// '◯', '⬤'
        /// </summary>
        Circles1,

        /// <summary>
        /// '⚪', '⚫'
        /// </summary>
        Circles2,
    }
}
