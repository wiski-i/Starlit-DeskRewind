using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace DeskRewind
{
    [DataContract]
    internal struct SavedBounds
    {
        [DataMember(Name = "x")] internal int X;
        [DataMember(Name = "y")] internal int Y;
        [DataMember(Name = "width")] internal int Width;
        [DataMember(Name = "height")] internal int Height;

        internal SavedBounds(Rectangle bounds)
        {
            X = bounds.X;
            Y = bounds.Y;
            Width = bounds.Width;
            Height = bounds.Height;
        }

        internal Rectangle Rectangle { get { return new Rectangle(X, Y, Width, Height); } }
    }

    [DataContract]
    internal sealed class WorkspaceScreen
    {
        [DataMember(Name = "device")] internal string Device;
        [DataMember(Name = "monitorId", EmitDefaultValue = false)] internal string MonitorId;
        [DataMember(Name = "bounds")] internal SavedBounds Bounds;
        [DataMember(Name = "workArea")] internal SavedBounds Work;

        internal WorkspaceScreen(Display display, bool identifyMonitor = false)
        {
            Device = display.Name;
            if (identifyMonitor) MonitorId = display.MonitorId;
            Bounds = new SavedBounds(display.Bounds);
            Work = new SavedBounds(display.Work);
        }

        internal string Label { get { return Device.Replace(@"\\.\DISPLAY", "Display "); } }
        internal Display Resolve(Display[] displays)
        {
            if (!string.IsNullOrEmpty(MonitorId))
            {
                var hardware = displays.FirstOrDefault(display => string.Equals(display.MonitorId, MonitorId, StringComparison.OrdinalIgnoreCase));
                if (hardware != null) return hardware;
            }
            var named = displays.FirstOrDefault(display => display.Name == Device);
            if (named != null && (string.IsNullOrEmpty(MonitorId) || string.IsNullOrEmpty(named.MonitorId))) return named;
            return Display.Nearest(displays, Bounds.Rectangle);
        }
        public override string ToString() { return Label; }
    }

    [DataContract]
    internal sealed class WorkspaceWindow
    {
        [DataMember(Name = "app")] internal string AppKey;
        [DataMember(Name = "executable")] internal string Executable;
        [DataMember(Name = "package", EmitDefaultValue = false)] internal string Package;
        [DataMember(Name = "title")] internal string Title;
        [DataMember(Name = "titleContains", EmitDefaultValue = false)] internal string TitleMatch { get; set; }
        [DataMember(Name = "launchTarget", EmitDefaultValue = false)] internal string LaunchTarget { get; set; }
        [DataMember(Name = "display")] internal string Device;
        [DataMember(Name = "frame")] internal SavedBounds Frame;
        [DataMember(Name = "normalBounds")] internal SavedBounds Normal;
        [DataMember(Name = "frameInsets")] internal SavedBounds Insets;
        [DataMember(Name = "state")] internal WindowState State;

        internal long SessionIdentity;
    }

    [DataContract]
    internal sealed class Workspace
    {
        [DataMember(Name = "id")] internal Guid Id = Guid.NewGuid();
        [DataMember(Name = "name")] internal string Name = "";
        [DataMember(Name = "launchMissing")] internal bool LaunchMissing;
        [DataMember(Name = "savedAt", EmitDefaultValue = false)] internal DateTime SavedAt;
        [DataMember(Name = "displays")] internal List<WorkspaceScreen> Screens = new List<WorkspaceScreen>();
        [DataMember(Name = "windows")] internal List<WorkspaceWindow> Windows = new List<WorkspaceWindow>();

        internal Workspace WithName(string name)
        {
            var copy = (Workspace)MemberwiseClone();
            copy.Name = name;
            return copy;
        }

        internal Workspace WithLaunchPreference(bool launch)
        {
            var copy = (Workspace)MemberwiseClone();
            copy.LaunchMissing = launch;
            return copy;
        }

        internal static Workspace FromDesktop(DesktopLayout layout, bool identifyMonitors = false)
        {
            return new Workspace { Screens = layout.Displays.Select(display => new WorkspaceScreen(display, identifyMonitors)).ToList() };
        }

        internal static Rectangle Fit(Rectangle bounds, Rectangle work)
        {
            int width = Math.Min(Math.Max(1, bounds.Width), work.Width);
            int height = Math.Min(Math.Max(1, bounds.Height), work.Height);
            return new Rectangle(Math.Max(work.Left, Math.Min(bounds.Left, work.Right - width)),
                Math.Max(work.Top, Math.Min(bounds.Top, work.Bottom - height)), width, height);
        }

        internal static Rectangle Remap(Rectangle bounds, Rectangle source, Rectangle destination)
        {
            if (source == destination) return bounds;
            double x = destination.Width / (double)source.Width;
            double y = destination.Height / (double)source.Height;
            return Fit(new Rectangle(destination.X + (int)Math.Round((bounds.X - source.X) * x),
                destination.Y + (int)Math.Round((bounds.Y - source.Y) * y),
                (int)Math.Round(bounds.Width * x), (int)Math.Round(bounds.Height * y)), destination);
        }

        public override string ToString() { return Name; }
    }

    internal sealed class WorkspaceLibrary
    {
        private readonly WorkspaceStore store;
        private List<Workspace> layouts = new List<Workspace>();
        internal string LoadError { get; private set; }
        internal string LoadNotice { get; private set; }

        internal WorkspaceLibrary(WorkspaceStore store)
        {
            this.store = store;
            try
            {
                string notice;
                layouts = store.Load(out notice);
                LoadNotice = notice;
            }
            catch (Exception exception)
            {
                if (!StorageError(exception)) throw;
                LoadError = "Could not read workspaces.json. " + exception.Message;
            }
        }

        internal Workspace[] Read() { return layouts.OrderByDescending(item => item.SavedAt).ToArray(); }

        internal Workspace Capture(DesktopLayout desktop)
        {
            var layout = Workspace.FromDesktop(desktop, true);
            layout.SavedAt = DateTime.UtcNow;
            layout.LaunchMissing = true;
            string label = "Layout " + layout.SavedAt.ToLocalTime().ToString("d MMM, HH:mm");
            layout.Name = label;
            for (int number = 2; layouts.Any(item => item.Name == layout.Name); number++) layout.Name = label + " (" + number + ")";
            foreach (var open in WorkspaceApps.ReadWindows(desktop))
                layout.Windows.Add(open.SaveTo(layout.Screens.First(screen => screen.Device == open.Layout.Display.Name)));
            Write(layouts.Concat(new[] { layout }).ToList());
            return layout;
        }

        internal bool Rename(Workspace layout, string name)
        {
            if (layouts.Any(item => item.Id != layout.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))) return false;
            Write(layouts.Select(item => item.Id == layout.Id ? item.WithName(name) : item).ToList());
            return true;
        }

        internal void Delete(Workspace layout) { Write(layouts.Where(item => item.Id != layout.Id).ToList()); }

        internal void SetLaunchPreference(Workspace layout, bool launch)
        {
            if (layout.LaunchMissing == launch) return;
            Write(layouts.Select(item => item.Id == layout.Id ? item.WithLaunchPreference(launch) : item).ToList());
            layout.LaunchMissing = launch;
        }

        private void Write(List<Workspace> updated)
        {
            if (LoadError != null) throw new InvalidDataException(LoadError);
            store.Save(updated);
            layouts = updated;
            LoadNotice = null;
        }

        internal static bool StorageError(Exception exception)
        {
            return exception is IOException || exception is UnauthorizedAccessException || exception is SerializationException
                || exception is System.Xml.XmlException || exception is NotSupportedException;
        }
    }

    internal sealed class WorkspaceStore
    {
        [DataContract]
        private sealed class Document
        {
            [DataMember(Name = "version")] internal int Version = 1;
            [DataMember(Name = "workspaces")] internal List<Workspace> Workspaces;
        }

        private readonly string path;
        private readonly DataContractJsonSerializer json = new DataContractJsonSerializer(typeof(Document));
        private bool recoveredFromBackup;

        internal WorkspaceStore(string path) { this.path = path; }

        internal List<Workspace> Load()
        {
            string notice;
            return Load(out notice);
        }

        internal List<Workspace> Load(out string notice)
        {
            notice = null;
            recoveredFromBackup = false;
            InvalidDataException corrupt = null;
            try
            {
                var primary = Read(path);
                if (primary != null) return primary;
            }
            catch (InvalidDataException exception) { corrupt = exception; }

            var recovered = Read(path + ".bak");
            if (recovered != null)
            {
                recoveredFromBackup = true;
                notice = "Saved layouts recovered from the previous backup.";
                return recovered;
            }
            if (corrupt != null) throw corrupt;
            return new List<Workspace>();
        }

        private List<Workspace> Read(string file)
        {
            try
            {
                using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                {
                    var data = (Document)json.ReadObject(input);
                    if (data != null && data.Version > 1)
                        throw new NotSupportedException("This file was created by a newer version of DeskRewind.");
                    if (data == null || data.Version != 1 || data.Workspaces == null || data.Workspaces.Any(Invalid)
                        || data.Workspaces.Select(workspace => workspace.Id).Distinct().Count() != data.Workspaces.Count)
                        throw new InvalidDataException("The workspace file has an invalid format.");
                    return data.Workspaces;
                }
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            catch (SerializationException exception) { throw new InvalidDataException("The workspace file is damaged.", exception); }
            catch (System.Xml.XmlException exception) { throw new InvalidDataException("The workspace file is damaged.", exception); }
        }

        private static bool Invalid(Workspace workspace)
        {
            return workspace == null || workspace.Id == Guid.Empty || string.IsNullOrWhiteSpace(workspace.Name)
                || workspace.Screens == null || workspace.Screens.Count == 0 || workspace.Windows == null
                || workspace.Screens.Any(screen => screen == null || string.IsNullOrWhiteSpace(screen.Device)
                    || screen.Bounds.Width <= 0 || screen.Bounds.Height <= 0 || screen.Work.Width <= 0 || screen.Work.Height <= 0)
                || workspace.Windows.Any(window => window == null || string.IsNullOrWhiteSpace(window.AppKey)
                    || string.IsNullOrWhiteSpace(window.Title) || window.Frame.Width <= 0 || window.Frame.Height <= 0
                    || !Enum.IsDefined(typeof(WindowState), window.State)
                    || !workspace.Screens.Any(screen => screen.Device == window.Device));
        }

        internal void Save(List<Workspace> workspaces)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string staging = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    json.WriteObject(output, new Document { Workspaces = workspaces });
                    output.Flush(true);
                }
                if (File.Exists(path))
                {
                    string backup = recoveredFromBackup ? path + ".corrupt-" + Guid.NewGuid().ToString("N") : path + ".bak";
                    File.Replace(staging, path, backup);
                }
                else File.Move(staging, path);
                recoveredFromBackup = false;
            }
            finally { if (File.Exists(staging)) File.Delete(staging); }
        }
    }
}
