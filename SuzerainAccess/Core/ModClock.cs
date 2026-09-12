using System.Diagnostics;

namespace SuzerainAccess.Core
{
    /// <summary>(Named ModClock because the game declares a global class "Clock".) One shared time base (seconds) so subsystems can compare timestamps.</summary>
    internal static class ModClock
    {
        public static float Now => (float)(Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);
    }
}
