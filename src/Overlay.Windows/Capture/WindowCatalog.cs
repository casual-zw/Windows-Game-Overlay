using System.Diagnostics;
using System.Text;
using Overlay.Windows.Interop;

namespace Overlay.Windows.Capture;

public sealed record WindowChoice(nint Handle, uint ProcessId, string Label);

internal static class WindowCatalog
{
    internal static List<WindowChoice> List(nint testWindow = 0)
    {
        var windows = new List<WindowChoice>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == Environment.ProcessId && hwnd != testWindow) return true;
            if (Native.GetCloaked(hwnd, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
            var title = new StringBuilder(512);
            Native.GetWindowText(hwnd, title, title.Capacity);
            if (title.Length == 0) return true;
            string processName;
            try { using var process = Process.GetProcessById((int)pid); processName = process.ProcessName; }
            catch (ArgumentException) { return true; }
            catch (System.ComponentModel.Win32Exception) { processName = $"PID {pid}"; }
            windows.Add(new(hwnd, pid, $"{title} — {processName}"));
            return true;
        }, 0);
        return windows.OrderBy(w => w.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}
