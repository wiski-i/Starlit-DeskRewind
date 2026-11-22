using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;

namespace DeskRewind
{
    internal static class WorkspaceRestore
    {
        private sealed class Slot
        {
            internal readonly WorkspaceWindow Saved;
            internal long Identity;
            internal int LaunchedProcess;
            internal WindowLayout Live;

            internal Slot(WorkspaceWindow saved) { Saved = saved; }
        }

        internal static async Task<string> Apply(Workspace workspace, bool launchMissing, Func<DesktopLayout> capture, Action<string> status)
        {
            var desktop = capture();
            if (desktop == null) return "Windows could not read the current displays.";
            var slots = SlotsFor(workspace);
            var initialWindows = WorkspaceApps.ReadWindows(desktop);
            var initial = new HashSet<long>(initialWindows.Select(window => window.Layout.Identity));
            var used = new HashSet<long>();
            var pending = new List<Slot>();

            MatchSlots(workspace, slots, initialWindows, desktop.Displays, used, null);
            Position(workspace, slots, desktop.Displays);
            if (launchMissing)
            {
                var requested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var slot in slots.Where(slot => slot.Identity == 0))
                {
                    if (string.IsNullOrWhiteSpace(slot.Saved.LaunchTarget) && string.IsNullOrWhiteSpace(slot.Saved.TitleMatch)
                        && initialWindows.Any(window => SameApp(slot, window) && !used.Contains(window.Layout.Identity))) continue;
                    bool launchOnce = WorkspaceApps.LaunchOnce(slot.Saved);
                    if (launchOnce && requested.Contains(slot.Saved.AppKey)) continue;
                    if (!WorkspaceApps.TryLaunch(slot.Saved, out slot.LaunchedProcess)) continue;
                    if (launchOnce) requested.Add(slot.Saved.AppKey);
                    pending.Add(slot);
                }
            }

            if (pending.Count != 0)
            {
                status("Waiting for " + pending.Count + (pending.Count == 1 ? " application to open…" : " applications to open…"));
                var elapsed = System.Diagnostics.Stopwatch.StartNew();
                while (pending.Count != 0 && elapsed.ElapsedMilliseconds < 12000)
                {
                    await Task.Delay(350);
                    desktop = capture();
                    if (desktop == null) continue;
                    MatchSlots(workspace, pending, WorkspaceApps.ReadWindows(desktop), desktop.Displays, used, initial);
                    Position(workspace, pending, desktop.Displays);
                    pending.RemoveAll(slot => slot.Identity != 0);
                }
            }

            await Task.Delay(350);
            desktop = capture();
            if (desktop == null) return "Windows could not verify the restored layout.";
            foreach (var slot in slots.Where(item => item.Identity != 0))
            {
                WindowLayout live;
                if (!desktop.ByIdentity.TryGetValue(slot.Identity, out live)) continue;
                var target = Target(workspace, slot.Saved, live, desktop.Displays);
                if (!IsPlaced(target, live)) DesktopWindows.Restore(target, live, desktop.Displays);
            }

            await Task.Delay(250);
            desktop = capture();
            int placed = 0;
            if (desktop != null)
            {
                foreach (var slot in slots.Where(item => item.Identity != 0))
                {
                    WindowLayout live;
                    if (desktop.ByIdentity.TryGetValue(slot.Identity, out live)
                        && IsPlaced(Target(workspace, slot.Saved, live, desktop.Displays), live)) placed++;
                }
            }
            int missing = slots.Count(slot => slot.Identity == 0);
            int blocked = slots.Count - placed - missing;
            int unmatched = slots.Count(slot => slot.Identity == 0 && initialWindows.Any(window =>
                SameApp(slot, window) && !used.Contains(window.Layout.Identity)));
            missing -= unmatched;
            string result = placed + " of " + slots.Count + " windows placed.";
            if (unmatched != 0) result += " " + unmatched + (unmatched == 1
                ? " open window could not be matched." : " open windows could not be matched.");
            if (missing != 0) result += " " + missing + (missing == 1 ? " window unavailable." : " windows unavailable.");
            if (blocked != 0) result += " " + blocked + (blocked == 1 ? " window could not be positioned." : " windows could not be positioned.");
            return result;
        }

        internal static Dictionary<WorkspaceWindow, WindowLayout> Inspect(Workspace workspace, DesktopLayout desktop,
            WorkspaceApps.OpenWindow[] open)
        {
            var slots = SlotsFor(workspace);
            MatchSlots(workspace, slots, open, desktop.Displays, new HashSet<long>(), null);
            return slots.Where(slot => slot.Live != null).ToDictionary(slot => slot.Saved, slot => slot.Live);
        }

        internal static bool IsAtSavedPosition(Workspace workspace, WorkspaceWindow saved, WindowLayout live, Display[] displays)
        {
            return IsPlaced(Target(workspace, saved, live, displays), live);
        }

        private static List<Slot> SlotsFor(Workspace workspace)
        {
            return workspace.Windows.OrderByDescending(window => !string.IsNullOrEmpty(window.TitleMatch))
                .Select(window => new Slot(window)).ToList();
        }

        private static void MatchSlots(Workspace workspace, List<Slot> slots, WorkspaceApps.OpenWindow[] open, Display[] displays,
            HashSet<long> used, HashSet<long> initial)
        {
            // Reserve remembered or newly launched windows before matching titles and generic app slots.
            foreach (var slot in slots.Where(item => item.Identity == 0))
            {
                var choices = Candidates(slot, open, used, initial).ToArray();
                var window = choices.FirstOrDefault(item => item.Layout.Identity == slot.Saved.SessionIdentity);
                if (window == null && initial != null && slot.LaunchedProcess > 0)
                {
                    var ownProcess = choices.Where(item => item.Layout.Process == (uint)slot.LaunchedProcess).ToArray();
                    window = ownProcess.FirstOrDefault(item => SameTitle(slot, item));
                }
                if (window != null) Bind(slot, window.Layout, used);
            }

            foreach (var slot in slots.Where(item => item.Identity == 0))
            {
                var exact = Candidates(slot, open, used, initial).Where(item => SameTitle(slot, item))
                    .OrderBy(item => Distance(workspace, slot.Saved, item.Layout, displays)).FirstOrDefault();
                if (exact != null) Bind(slot, exact.Layout, used);
            }

            foreach (var slot in slots.Where(item => item.Identity == 0))
            {
                if (initial != null && slot.LaunchedProcess > 0)
                {
                    var ownProcess = Candidates(slot, open, used, initial)
                        .Where(item => item.Layout.Process == (uint)slot.LaunchedProcess).Take(2).ToArray();
                    if (ownProcess.Length == 1)
                    {
                        Bind(slot, ownProcess[0].Layout, used);
                        continue;
                    }
                }
                bool hasTitleHint = !string.IsNullOrEmpty(slot.Saved.TitleMatch);
                if (!hasTitleHint && initial == null && WorkspaceApps.WebAddress(slot.Saved.LaunchTarget) != null) continue;
                if (!hasTitleHint && slots.Count(other => other.Identity == 0 && string.Equals(other.Saved.AppKey,
                    slot.Saved.AppKey, StringComparison.OrdinalIgnoreCase)) != 1) continue;
                var choices = Candidates(slot, open, used, initial).Take(2).ToArray();
                if (choices.Length == 1) Bind(slot, choices[0].Layout, used);
            }
        }

        private static IEnumerable<WorkspaceApps.OpenWindow> Candidates(Slot slot, WorkspaceApps.OpenWindow[] open,
            HashSet<long> used, HashSet<long> initial)
        {
            return open.Where(window => !used.Contains(window.Layout.Identity)
                && (SameApp(slot, window) || (initial != null && slot.LaunchedProcess > 0
                    && !string.IsNullOrEmpty(slot.Saved.LaunchTarget) && window.Layout.Process == (uint)slot.LaunchedProcess))
                && (string.IsNullOrEmpty(slot.Saved.TitleMatch)
                    || window.Layout.Title.IndexOf(slot.Saved.TitleMatch, StringComparison.OrdinalIgnoreCase) >= 0)
                && (initial == null || !initial.Contains(window.Layout.Identity) || !string.IsNullOrEmpty(slot.Saved.TitleMatch)));
        }

        private static bool SameApp(Slot slot, WorkspaceApps.OpenWindow window)
        {
            return string.Equals(window.AppKey, slot.Saved.AppKey, StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameTitle(Slot slot, WorkspaceApps.OpenWindow window)
        {
            return string.Equals(window.Layout.Title, slot.Saved.Title, StringComparison.OrdinalIgnoreCase);
        }

        private static long Distance(Workspace workspace, WorkspaceWindow saved, WindowLayout live, Display[] displays)
        {
            var target = Target(workspace, saved, live, displays).Frame;
            var source = live.State == WindowState.Minimized ? live.Normal : live.Frame;
            return Math.Abs((long)target.X - source.X) + Math.Abs((long)target.Y - source.Y)
                + Math.Abs((long)target.Width - source.Width) + Math.Abs((long)target.Height - source.Height);
        }

        private static void Bind(Slot slot, WindowLayout live, HashSet<long> used)
        {
            used.Add(live.Identity);
            slot.Identity = live.Identity;
            slot.Live = live;
        }

        private static void Position(Workspace workspace, IEnumerable<Slot> slots, Display[] displays)
        {
            foreach (var slot in slots.Where(item => item.Live != null))
            {
                var target = Target(workspace, slot.Saved, slot.Live, displays);
                if (!IsPlaced(target, slot.Live)) DesktopWindows.Restore(target, slot.Live, displays);
            }
        }

        private static WindowLayout Target(Workspace workspace, WorkspaceWindow saved, WindowLayout live, Display[] displays)
        {
            var screen = workspace.Screens.First(item => item.Device == saved.Device);
            var display = screen.Resolve(displays);
            Rectangle frame = Workspace.Remap(saved.Frame.Rectangle, screen.Work.Rectangle, display.Work);
            Rectangle normal = saved.Normal.Width > 0 && saved.Normal.Height > 0
                ? Workspace.Remap(saved.Normal.Rectangle, screen.Work.Rectangle, display.Work) : frame;
            var insets = saved.Insets;
            if (live.State == WindowState.Normal)
                insets = new SavedBounds(new Rectangle(live.Frame.Left - live.Outer.Left, live.Frame.Top - live.Outer.Top,
                    live.Outer.Right - live.Frame.Right, live.Outer.Bottom - live.Frame.Bottom));
            Rectangle outer = Rectangle.FromLTRB(frame.Left - insets.X, frame.Top - insets.Y,
                frame.Right + insets.Width, frame.Bottom + insets.Height);
            return new WindowLayout { Display = display, Frame = frame, State = saved.State, Outer = outer, Normal = normal };
        }

        private static bool IsPlaced(WindowLayout target, WindowLayout current)
        {
            if (current.State != target.State || current.Display.Name != target.Display.Name) return false;
            return target.State == WindowState.Normal ? CloseBounds(target.Frame, current.Frame) : CloseBounds(target.Normal, current.Normal);
        }

        private static bool CloseBounds(Rectangle target, Rectangle current)
        {
            const int tolerance = 4;
            return Math.Abs((long)target.Left - current.Left) <= tolerance
                && Math.Abs((long)target.Top - current.Top) <= tolerance
                && Math.Abs((long)target.Right - current.Right) <= tolerance
                && Math.Abs((long)target.Bottom - current.Bottom) <= tolerance;
        }
    }
}
