using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.1.1: keeps every menu and submenu off the Windows taskbar.
    // A ContextMenuStrip shown with Show(Point) has no owner window, and WinForms drop-downs don't set WS_EX_TOOLWINDOW,
    // so Windows gave the menu (and each submenu it opened) its own taskbar button. Menus are now shown with the beacon
    // as their source control (owned by the beacon's tool window), and every drop-down, including submenus that are
    // added later, gets WS_EX_TOOLWINDOW before it is shown.
    static class NoTaskbar
    {
        const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] static extern int GetWindowLong32(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] static extern int SetWindowLong32(IntPtr h, int i, int v);

        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripDropDown, object> hooked = new System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripDropDown, object>();

        public static void Fix(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            int ex = GetWindowLong32(h, GWL_EXSTYLE);
            int want = (ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
            if (want != ex) SetWindowLong32(h, GWL_EXSTYLE, want);
        }

        // hook a menu (or submenu) and, recursively, every submenu it has now or gets later
        public static T Hook<T>(T dd) where T : ToolStripDropDown
        {
            if (dd == null) return dd;
            object seen; if (hooked.TryGetValue(dd, out seen)) return dd;
            hooked.Add(dd, null);
            dd.HandleCreated += (s, e) => Fix(dd.Handle);
            if (dd.IsHandleCreated) Fix(dd.Handle);
            dd.Opening += (s, e) => { if (dd.IsHandleCreated) Fix(dd.Handle); HookItems(dd.Items); };
            dd.ItemAdded += (s, e) => HookItem(e.Item);
            HookItems(dd.Items);
            return dd;
        }
        static void HookItems(ToolStripItemCollection items) { foreach (ToolStripItem it in items) HookItem(it); }
        // 1.1.3: each item is hooked once; reused menus used to gain one more DropDownOpening handler per item on every open
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripItem, object> hookedItems = new System.Runtime.CompilerServices.ConditionalWeakTable<ToolStripItem, object>();
        static void HookItem(ToolStripItem it)
        {
            var di = it as ToolStripDropDownItem;
            if (di == null) return;
            object seen; if (hookedItems.TryGetValue(di, out seen)) return;
            hookedItems.Add(di, null);
            di.DropDownOpening += (s, e) => Hook(di.DropDown);   // the submenu's items may be filled in here
            if (di.HasDropDownItems) Hook(di.DropDown);
        }
    }
}
