namespace Yaap
{
    /// <summary>
    /// A class representing the various colors that can be applied to Yaap
    /// </summary>
    public class YaapColorScheme
    {
        /// <summary>
        /// The <see cref="TerminalColor"/> of the <see cref="YaapElement.ProgressBar"/> element when in <see cref="YaapState.Running"/>
        /// </summary>
        public TerminalColor ProgressBarColor { get; set; } = TerminalColor.None;

        /// <summary>
        /// The <see cref="TerminalColor"/> of the <see cref="YaapElement.ProgressBar"/> element when in <see cref="YaapState.Paused"/>
        /// </summary>
        public TerminalColor ProgressBarPausedColor { get; set; } = TerminalColor.None;


        /// <summary>
        /// The <see cref="TerminalColor"/> of the <see cref="YaapElement.ProgressBar"/> element when in <see cref="YaapState.Stalled"/>
        /// </summary>
        public TerminalColor ProgressBarStalledColor { get; set; } = TerminalColor.None;

        /// <summary>
        /// The <see cref="TerminalColor"/> of the <see cref="YaapElement.ProgressPercent"/> element
        /// </summary>
        public TerminalColor ProgressPercentColor { get; set; } = TerminalColor.None;

        /// <summary>
        /// The <see cref="TerminalColor"/> of the <see cref="YaapElement.ProgressCount"/> element
        /// </summary>
        public TerminalColor ProgressCountColor { get; set; } = TerminalColor.None;

        /// <summary>
        /// The <see cref="TerminalColor"/> of the <see cref="YaapElement.Rate"/> element
        /// </summary>
        public TerminalColor RateColor { get; set; } = TerminalColor.None;

        /// <summary>
        /// The <see cref="TerminalColor"/> of the <see cref="YaapElement.Time"/> element
        /// </summary>
        public TerminalColor TimeColor { get; set; } = TerminalColor.None;

        /// <summary>
        /// The "no-color" color scheme for Yaap
        /// </summary>
        public static readonly YaapColorScheme NoColor = new YaapColorScheme
        {
            ProgressBarColor = TerminalColor.None,
            ProgressPercentColor = TerminalColor.None,
        };

        /// <summary>
        /// The Bright Yaap color scheme for Yaap
        /// </summary>
        public static readonly YaapColorScheme Bright = new YaapColorScheme
        {
            ProgressBarColor = TerminalColor.FromConsoleColor(AnsiColor.BrightGreen),
            ProgressBarPausedColor = TerminalColor.FromConsoleColor(AnsiColor.BrightYellow),
            ProgressBarStalledColor = TerminalColor.FromConsoleColor(AnsiColor.Red),
            ProgressPercentColor = TerminalColor.FromConsoleColor(AnsiColor.BrightYellow),
            ProgressCountColor = TerminalColor.FromConsoleColor(AnsiColor.BrightMagenta),
            RateColor = TerminalColor.FromConsoleColor(AnsiColor.BrightCyan),
            TimeColor = TerminalColor.FromConsoleColor(AnsiColor.BrightGreen),
        };

        /// <summary>
        /// The Bright Yaap color scheme for Yaap
        /// </summary>
        public static readonly YaapColorScheme Dark = new YaapColorScheme
        {
            ProgressBarColor = TerminalColor.FromConsoleColor(AnsiColor.Green),
            ProgressBarPausedColor = TerminalColor.FromConsoleColor(AnsiColor.Yellow),
            ProgressBarStalledColor = TerminalColor.FromConsoleColor(AnsiColor.Red),
            ProgressPercentColor = TerminalColor.FromConsoleColor(AnsiColor.Yellow),
            ProgressCountColor = TerminalColor.FromConsoleColor(AnsiColor.Magenta),
            RateColor = TerminalColor.FromConsoleColor(AnsiColor.Cyan),
            TimeColor = TerminalColor.FromConsoleColor(AnsiColor.Green),
        };
    }
}
