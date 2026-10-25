using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace BotBeacons
{
    // 1.2.4: "click into the Add tab dialog and the shelf slides shut, taking the dialog with it". The shelf's click-away
    // check only knew about the Edit tab form, so a press inside the New tab dialog counted as a click elsewhere, and a
    // dialog with no owner given took whatever window was active as its owner. Now every dialog the shelf, a card or a
    // beacon opens is tracked here: while any is open (a reference count), the shelf and cards stay out; a press on one
    // of them is never "elsewhere"; deactivation that moves to one of them doesn't collapse a card or bubble. Dialogs are
    // owned by a hidden top-level window that never closes, never by the sliding shelf or a card.
    static class ModalHold
    {
        static int count;
        static readonly HashSet<Form> tracked = new HashSet<Form>();
        public static int Count { get { return count; } }
        public static bool Held { get { return count > 0; } }

        sealed class Token : IDisposable
        {
            bool done; readonly string why;
            public Token(string why) { this.why = why; }
            public void Dispose() { if (done) return; done = true; count = Math.Max(0, count - 1); if (Settings.DevHooks) Log.Write("Modal hold released (" + why + "), " + count + " left."); }
        }
        public static IDisposable Hold(string why) { count++; if (Settings.DevHooks) Log.Write("Modal hold (" + why + "), " + count + " open."); return new Token(why); }

        // hold for as long as this form is open; a press on it or focus moving to it is "ours"
        public static void Track(Form f, string why)
        {
            var h = Hold(why); tracked.Add(f);
            EventHandler drop = null;
            drop = (s, e) => { tracked.Remove(f); h.Dispose(); };
            f.FormClosed += (s, e) => drop(s, e);
            f.Disposed += drop;
        }

        public static bool OwnDialogAt(Point screen)
        {
            foreach (var f in tracked) if (!f.IsDisposed && f.Visible && f.Bounds.Contains(screen)) return true;
            return false;
        }
        public static bool OwnDialogActive()
        {
            var a = Form.ActiveForm;
            return a != null && tracked.Contains(a);
        }

        static Form owner;
        // a stable top-level owner for dialogs: hidden, never shown, never closed
        public static IWin32Window Owner
        {
            get
            {
                if (owner == null || owner.IsDisposed)
                {
                    owner = new Form { ShowInTaskbar = false, FormBorderStyle = FormBorderStyle.None, StartPosition = FormStartPosition.Manual, Location = new Point(-32000, -32000), Size = new Size(1, 1), Text = "Bot Beacons" };
                    var h = owner.Handle;   // created, not shown
                }
                return owner;
            }
        }

        // ---- the rules (pure; tools/ClickHarness.cs.txt drives them)
        // click mode: does this press close the open shelf?
        public static bool PressClosesShelf(bool press, bool insideShelf, bool menusOpen, bool held, bool onOwnDialog, bool onItsGrip)
        {
            return press && !insideShelf && !menusOpen && !held && !onOwnDialog && !onItsGrip;
        }
        // a card / bubble lost activation: close it as "clicked away"?
        public static bool DeactivateCloses(bool held, bool focusToOwnDialog, bool cursorInside, double msSinceOpened)
        {
            if (msSinceOpened < 600) return false;
            return !held && !focusToOwnDialog && !cursorInside;
        }
    }
}
