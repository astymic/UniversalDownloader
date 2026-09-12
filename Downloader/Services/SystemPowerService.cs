using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UniversalDownloader.Services
{
    public enum PowerAction
    {
        Shutdown,
        Sleep,
        Hibernate
    }

    public static class SystemPowerService
    {
        [DllImport("powrprof.dll", SetLastError = true)]
        private static extern bool SetSuspendState(bool hibernate, bool forceCritical, bool disableWakeEvent);

        public static bool ExecutePowerAction(PowerAction action)
        {
            try
            {
                switch (action)
                {
                    case PowerAction.Shutdown:
                        var psi = new ProcessStartInfo
                        {
                            FileName = "shutdown.exe",
                            Arguments = "/s /t 0",
                            CreateNoWindow = true,
                            UseShellExecute = false
                        };
                        Process.Start(psi);
                        return true;

                    case PowerAction.Sleep:
                        return SetSuspendState(false, true, false);

                    case PowerAction.Hibernate:
                        return SetSuspendState(true, true, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SystemPowerService] Failed to execute {action}: {ex.Message}");
            }
            return false;
        }

        public static string GetActionDisplayName(PowerAction action)
        {
            return action switch
            {
                PowerAction.Shutdown => "Shut Down",
                PowerAction.Sleep => "Sleep",
                PowerAction.Hibernate => "Hibernate",
                _ => "Shut Down"
            };
        }

        public static PowerAction ParseFromIndex(int index)
        {
            return index switch
            {
                0 => PowerAction.Shutdown,
                1 => PowerAction.Sleep,
                2 => PowerAction.Hibernate,
                _ => PowerAction.Shutdown
            };
        }
    }
}
