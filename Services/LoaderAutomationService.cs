using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace Sigil.Services;

/// <summary>
/// Automates the loader UI: find the process table row for the given PID,
/// right-click that row, then select "inject" from the context menu.
/// Loader has columns: pid, data, type, state. Context menu: "inject", "force close".
/// </summary>
public sealed class LoaderAutomationService
{
    private const int WaitForLoaderSeconds = 15;
    private const int WaitAfterRightClickMs = 400;
    private const int WaitForMenuMs = 300;

    /// <summary>
    /// Starts the loader exe (if path given), waits for its window, finds the row
    /// whose pid column matches <paramref name="gameProcessId"/>, right-clicks that row,
    /// then selects "inject" from the context menu.
    /// </summary>
    /// <param name="loaderExePath">Full path to loader.exe. If null/empty, only inject is attempted (loader must already be running).</param>
    /// <param name="gameProcessId">Process ID of the game client – used to find the correct table row.</param>
    public async Task LaunchLoaderAndInjectAsync(string? loaderExePath, int gameProcessId)
    {
        Process? loaderProcess = null;

        if (!string.IsNullOrWhiteSpace(loaderExePath) && File.Exists(loaderExePath))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = loaderExePath,
                UseShellExecute = true
            };
            loaderProcess = Process.Start(startInfo);
            if (loaderProcess == null)
                throw new InvalidOperationException("Failed to start loader.");
        }

        IntPtr loaderWindowHandle;
        if (loaderProcess != null)
        {
            loaderWindowHandle = await WaitForMainWindowAsync(loaderProcess, TimeSpan.FromSeconds(WaitForLoaderSeconds)).ConfigureAwait(false);
            if (loaderWindowHandle == IntPtr.Zero)
                throw new InvalidOperationException("Loader window did not appear in time. Open the loader manually and use right-click → inject.");
            await Task.Delay(2000).ConfigureAwait(false); // let loader populate process list
        }
        else
        {
            loaderWindowHandle = FindLoaderWindow();
            if (loaderWindowHandle == IntPtr.Zero)
                throw new InvalidOperationException("Loader window not found. Start the loader first.");
        }

        await Task.Run(() =>
        {
            try
            {
                RightClickPidRowAndSelectInject(loaderWindowHandle, gameProcessId);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not trigger inject in loader UI: {ex.Message}. Use right-click → inject manually.", ex);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds the table row for the given PID, right-clicks it, then selects "inject".
    /// Tries UI Automation first (table + row by PID); falls back to right-click at window offset + key I.
    /// </summary>
    public void RightClickPidRowAndSelectInject(IntPtr loaderWindowHandle, int gameProcessId)
    {
        var pidStr = gameProcessId.ToString();

        if (TryFindRowByPidAndInject(loaderWindowHandle, pidStr))
            return;

        // Fallback: right-click near center-left (where first pid column often is), then press I for "inject"
        FallbackRightClickAndInject(loaderWindowHandle);
    }

    private static bool TryFindRowByPidAndInject(IntPtr loaderWindowHandle, string pidStr)
    {
        AutomationElement? root = null;
        try
        {
            root = AutomationElement.FromHandle(loaderWindowHandle);
        }
        catch
        {
            return false;
        }

        if (root == null) return false;

        // Find table/list (columns: pid, data, type, state)
        var tableCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Table);
        var listCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List);
        var dataGridCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataGrid);

        var tableOrList = root.FindFirst(TreeScope.Descendants, tableCondition)
            ?? root.FindFirst(TreeScope.Descendants, dataGridCondition)
            ?? root.FindFirst(TreeScope.Descendants, listCondition);

        if (tableOrList == null) return false;

        // Find row (DataItem, TableRow, or ListItem) whose text/name contains our PID
        var rowConditions = new[]
        {
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.DataItem),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem)
        };

        AutomationElement? targetRow = null;
        foreach (var rowCond in rowConditions)
        {
            var rows = tableOrList.FindAll(TreeScope.Children, rowCond);
            foreach (AutomationElement row in rows)
            {
                var name = row.Current.Name ?? "";
                // Row might show "34356" or "34356  Unknown  Unknown  Unmanaged" etc.
                if (name.IndexOf(pidStr, StringComparison.Ordinal) >= 0)
                {
                    targetRow = row;
                    break;
                }
            }
            if (targetRow != null) break;
        }

        if (targetRow == null) return false;

        var rect = targetRow.Current.BoundingRectangle;
        if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) return false;

        // Right-click center of the pid row
        var x = rect.Left + rect.Width / 2;
        var y = rect.Top + rect.Height / 2;
        RightClickAt(x, y);
        Thread.Sleep(WaitAfterRightClickMs);

        // Select "inject" – try UIA menu first, then keyboard I
        if (ClickMenuItemContaining("inject"))
            return true;

        SendKey(Key.I);
        return true;
    }

    private static bool ClickMenuItemContaining(string text)
    {
        var menuCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Menu);
        var root = AutomationElement.RootElement;
        var menus = root.FindAll(TreeScope.Children, menuCondition);
        foreach (AutomationElement menu in menus)
        {
            var itemCondition = new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.MenuItem);
            var items = menu.FindAll(TreeScope.Descendants, itemCondition);
            foreach (AutomationElement item in items)
            {
                if ((item.Current.Name ?? "").IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var rect = item.Current.BoundingRectangle;
                    if (!rect.IsEmpty)
                    {
                        ClickAt(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static void FallbackRightClickAndInject(IntPtr windowHandle)
    {
        if (!GetWindowRect(windowHandle, out var r))
            return;
        // Right-click near left quarter (pid column) and vertical center of content area
        var x = r.Left + (r.Right - r.Left) / 4;
        var y = r.Top + (r.Bottom - r.Top) / 2;
        RightClickAt(x, y);
        Thread.Sleep(WaitForMenuMs);
        SendKey(Key.I);
    }

    private static IntPtr FindLoaderWindow()
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows((hWnd, _) =>
        {
            var length = GetWindowTextLength(hWnd) + 1;
            if (length <= 1) return true;
            var buf = new char[length];
            if (GetWindowText(hWnd, buf, length) == 0) return true;
            var title = new string(buf).TrimEnd('\0');
            // Title like "coaeasy | 9101 Days Left | 1.0.0" or "BWU Loader | ..."
            if (title.IndexOf("Loader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("coaeasy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                title.IndexOf("Days Left", StringComparison.Ordinal) >= 0)
            {
                found = hWnd;
                return false; // stop enum
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static async Task<IntPtr> WaitForMainWindowAsync(Process process, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            process.Refresh();
            if (process.MainWindowHandle != IntPtr.Zero)
                return process.MainWindowHandle;
            await Task.Delay(300).ConfigureAwait(false);
        }
        return IntPtr.Zero;
    }

    private static void RightClickAt(double x, double y)
    {
        SetCursorPos((int)x, (int)y);
        Thread.Sleep(50);
        var inputDown = new INPUT { type = InputTypeMouse, mi = new MOUSEINPUT { dwFlags = MouseEventFRightDown } };
        var inputUp = new INPUT { type = InputTypeMouse, mi = new MOUSEINPUT { dwFlags = MouseEventFRightUp } };
        SendInput(1, new[] { inputDown }, Marshal.SizeOf<INPUT>());
        SendInput(1, new[] { inputUp }, Marshal.SizeOf<INPUT>());
    }

    private static void ClickAt(double x, double y)
    {
        SetCursorPos((int)x, (int)y);
        Thread.Sleep(50);
        var inputDown = new INPUT { type = InputTypeMouse, mi = new MOUSEINPUT { dwFlags = MouseEventFLeftDown } };
        var inputUp = new INPUT { type = InputTypeMouse, mi = new MOUSEINPUT { dwFlags = MouseEventFLeftUp } };
        SendInput(1, new[] { inputDown }, Marshal.SizeOf<INPUT>());
        SendInput(1, new[] { inputUp }, Marshal.SizeOf<INPUT>());
    }

    private static void SendKey(Key key)
    {
        var vk = KeyInterop.VirtualKeyFromKey(key);
        var inputDown = new INPUT { type = InputTypeKeyboard, ki = new KEYBDINPUT { wVk = (ushort)vk } };
        var inputUp = new INPUT { type = InputTypeKeyboard, ki = new KEYBDINPUT { wVk = (ushort)vk, dwFlags = KeyEventFKeyUp } };
        SendInput(1, new[] { inputDown }, Marshal.SizeOf<INPUT>());
        SendInput(1, new[] { inputUp }, Marshal.SizeOf<INPUT>());
    }

    private const int InputTypeMouse = 0;
    private const int InputTypeKeyboard = 1;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint KeyEventFKeyUp = 0x0002;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, char[] lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public MOUSEINPUT mi;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }
}
