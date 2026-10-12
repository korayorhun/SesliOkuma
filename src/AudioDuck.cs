using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace SesliOkuma
{
    // Optional "lower other sounds while reading": other audio sessions go to 40% and come back afterwards.
    // Windows remembers per-app volume, so the originals are also written to disk: if the app ever dies while
    // ducked, the next start puts every session back (RecoverAfterCrash). Any failure leaves the system as it was.
    public static class AudioDuck
    {
        const float DuckTo = 0.4f;
        static readonly object Gate = new object();
        static List<DuckedSession> _ducked;

        sealed class DuckedSession { public string Id; public ISimpleAudioVolume Volume; public float Original, Ducked; }

        static string StatePath { get { return Path.Combine(AppSettings.DataDir, "ducked.txt"); } }

        public static void Set(bool duck)
        {
            lock (Gate)
            {
                try
                {
                    if (duck) Apply();
                    else Restore();
                }
                catch (Exception ex) { Logger.Log("duck: " + ex.Message); try { Restore(); } catch { } }
            }
        }

        static void ForEachSession(Action<string, uint, ISimpleAudioVolume> visit)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            IMMDevice device;
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out device) != 0) return;     // eRender, eMultimedia
            Guid iidMgr = typeof(IAudioSessionManager2).GUID;
            object mgrObj;
            device.Activate(ref iidMgr, 23, IntPtr.Zero, out mgrObj);                  // CLSCTX_ALL
            var manager = (IAudioSessionManager2)mgrObj;
            IAudioSessionEnumerator sessions;
            manager.GetSessionEnumerator(out sessions);
            int count;
            sessions.GetCount(out count);
            for (int i = 0; i < count; i++)
            {
                IAudioSessionControl control;
                if (sessions.GetSession(i, out control) != 0) continue;
                var control2 = control as IAudioSessionControl2;
                var volume = control as ISimpleAudioVolume;
                if (control2 == null || volume == null) continue;
                uint pid;
                string id;
                control2.GetProcessId(out pid);
                control2.GetSessionIdentifier(out id);
                visit(id ?? "", pid, volume);
            }
        }

        static void Apply()
        {
            if (_ducked != null) return;
            var list = new List<DuckedSession>();
            uint ownPid = (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
            ForEachSession(delegate(string id, uint pid, ISimpleAudioVolume volume)
            {
                if (pid == 0 || pid == ownPid) return;
                float level;
                if (volume.GetMasterVolume(out level) != 0 || level <= DuckTo) return;
                float ducked = DuckTo * level;
                if (volume.SetMasterVolume(ducked, Guid.Empty) == 0)
                    list.Add(new DuckedSession { Id = id, Volume = volume, Original = level, Ducked = ducked });
            });
            _ducked = list;
            if (list.Count == 0) return;
            try
            {
                var lines = new List<string>();
                foreach (var s in list)
                    lines.Add(s.Id + "\t" + s.Original.ToString("R", CultureInfo.InvariantCulture) + "\t" + s.Ducked.ToString("R", CultureInfo.InvariantCulture));
                File.WriteAllLines(StatePath, lines.ToArray());
            }
            catch { }
            Logger.Log("ducked " + list.Count + " sessions");
        }

        static void Restore()
        {
            var list = _ducked;
            _ducked = null;
            if (list == null) return;
            int restored = 0;
            foreach (var s in list)
                try { if (s.Volume.SetMasterVolume(s.Original, Guid.Empty) == 0) restored++; } catch { }
            try { if (File.Exists(StatePath)) File.Delete(StatePath); } catch { }
            if (list.Count > 0) Logger.Log("restored " + restored + "/" + list.Count + " sessions");
        }

        // A previous run ended while other apps were lowered: put back the ones still sitting at our ducked level.
        public static void RecoverAfterCrash()
        {
            lock (Gate)
            {
                try
                {
                    if (!File.Exists(StatePath)) return;
                    var saved = new Dictionary<string, float[]>();
                    foreach (string line in File.ReadAllLines(StatePath))
                    {
                        var parts = line.Split('\t');
                        float original, ducked;
                        if (parts.Length == 3 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out original)
                            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out ducked))
                            saved[parts[0]] = new[] { original, ducked };
                    }
                    int fixedCount = 0;
                    ForEachSession(delegate(string id, uint pid, ISimpleAudioVolume volume)
                    {
                        float[] levels;
                        float current;
                        if (!saved.TryGetValue(id, out levels) || volume.GetMasterVolume(out current) != 0) return;
                        if (Math.Abs(current - levels[1]) < 0.03f && volume.SetMasterVolume(levels[0], Guid.Empty) == 0) fixedCount++;
                    });
                    File.Delete(StatePath);
                    Logger.Log("duck recovery: restored " + fixedCount + " sessions");
                }
                catch (Exception ex) { Logger.Log("duck recovery: " + ex.Message); }
            }
        }

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        class MMDeviceEnumerator { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDeviceEnumerator
        {
            int NotImpl1();
            int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IMMDevice
        {
            int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        }

        [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionManager2
        {
            int NotImpl1();
            int NotImpl2();
            int GetSessionEnumerator(out IAudioSessionEnumerator enumerator);
        }

        [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionEnumerator
        {
            int GetCount(out int count);
            int GetSession(int index, out IAudioSessionControl session);
        }

        [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl
        {
            int NotImpl1();
        }

        [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IAudioSessionControl2
        {
            int NotImpl1(); int NotImpl2(); int NotImpl3(); int NotImpl4(); int NotImpl5();
            int NotImpl6(); int NotImpl7(); int NotImpl8(); int NotImpl9();
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
            int GetProcessId(out uint pid);
        }

        [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ISimpleAudioVolume
        {
            int SetMasterVolume(float level, [MarshalAs(UnmanagedType.LPStruct)] Guid eventContext);
            int GetMasterVolume(out float level);
        }
    }
}
