using System;
using System.ComponentModel;
using System.Diagnostics;

namespace NoFences.Model
{
    public sealed class DesktopAccessClient
    {
        public int ProcessId { get; set; }
        public long StartTimeUtcTicks { get; set; }

        internal bool Matches(DesktopAccessClient other)
        {
            return other != null && ProcessId == other.ProcessId && StartTimeUtcTicks == other.StartTimeUtcTicks;
        }

        internal bool IsRunning()
        {
            try
            {
                using (var process = Process.GetProcessById(ProcessId))
                    return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == StartTimeUtcTicks;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (Win32Exception) { return true; } // Keep paths stable when access cannot be checked.
        }
    }
}
