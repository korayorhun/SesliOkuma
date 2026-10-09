using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace SesliOkuma
{
    // Local Turkish neural voice: EMA Lightning (Canberk Aslan, Apache-2.0) + normalizer-tr (Erdem Tuna, Apache-2.0)
    // on ONNX Runtime, through the SesliOkuma.TrVoice bridge DLL. Everything lives in %LOCALAPPDATA%\SesliOkuma\ema,
    // installed on demand; the app runs fine without it.
    public static class LocalTrVoice
    {
        public const string VoiceId = "local:ema-tr";
        public const string PackageUrl = "https://github.com/korayorhun/SesliOkuma/releases/download/trvoice-ema-1/SesliOkuma-TrVoice-Ema.zip";
        public const string CreditsUrl = "https://github.com/korayorhun/ema-lightning-dotnet";

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr LoadLibrary(string path);

        static readonly object Gate = new object();
        static bool _resolverHooked, _loaded;
        static string _loadError;
        static Type _bridge;
        static MethodInfo _mRate, _mInfo, _mPrepare, _mRange, _mSynth, _mWav;
        static LocalSession _preview;

        public static string Dir { get { return Path.Combine(AppSettings.DataDir, "ema"); } }
        static string ModelsDir { get { return Path.Combine(Dir, "models"); } }

        public static bool IsInstalled
        {
            get
            {
                return Environment.Is64BitProcess
                    && File.Exists(Path.Combine(Dir, "SesliOkuma.TrVoice.dll"))
                    && File.Exists(Path.Combine(Dir, "onnxruntime.dll"))
                    && File.Exists(Path.Combine(ModelsDir, "text.onnx"));
            }
        }

        // Loads the bridge and the model once; returns null on success, otherwise the reason.
        public static string EnsureLoaded()
        {
            lock (Gate)
            {
                if (_loaded) return null;
                if (_loadError != null) return _loadError;
                if (!IsInstalled) { return "voice package not installed"; }
                try
                {
                    if (!_resolverHooked)
                    {
                        _resolverHooked = true;
                        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
                        {
                            try
                            {
                                string name = new AssemblyName(args.Name).Name + ".dll";
                                string candidate = Path.Combine(Dir, name);
                                return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
                            }
                            catch { return null; }
                        };
                    }
                    LoadLibrary(Path.Combine(Dir, "onnxruntime.dll"));
                    LoadLibrary(Path.Combine(Dir, "normalizer_tr.dll"));
                    var asm = Assembly.LoadFrom(Path.Combine(Dir, "SesliOkuma.TrVoice.dll"));
                    _bridge = asm.GetType("SesliOkuma.TrVoice.EmaTts", true);
                    var load = _bridge.GetMethod("Load");
                    _mRate = _bridge.GetMethod("GetSampleRate");
                    _mInfo = _bridge.GetMethod("GetInfo");
                    _mPrepare = _bridge.GetMethod("Prepare");
                    _mRange = _bridge.GetMethod("GetChunkRange");
                    _mSynth = _bridge.GetMethod("Synth");
                    _mWav = _bridge.GetMethod("SynthToWav");
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    string error = (string)load.Invoke(null, new object[] { ModelsDir, 4 });
                    if (!string.IsNullOrEmpty(error)) { _loadError = error; Logger.Log("ema load failed: " + error); return error; }
                    _loaded = true;
                    Logger.Log("ema loaded in " + sw.ElapsedMilliseconds + " ms; " + Info);
                    return null;
                }
                catch (Exception ex)
                {
                    _loadError = ex.Message;
                    Logger.Log("ema load failed: " + ex);
                    return _loadError;
                }
            }
        }

        // Background warm-up so the first read does not pay the model load.
        public static void WarmUpAsync()
        {
            if (!IsInstalled) return;
            ThreadPool.QueueUserWorkItem(delegate { EnsureLoaded(); });
        }

        public static int SampleRate { get { return _loaded ? (int)_mRate.Invoke(null, null) : 48000; } }
        public static string Info { get { return _loaded ? (string)_mInfo.Invoke(null, null) : ""; } }

        public static int Prepare(string text, double speed) { return (int)_mPrepare.Invoke(null, new object[] { text, speed }); }
        public static int[] ChunkRange(int index) { return (int[])_mRange.Invoke(null, new object[] { index }); }
        public static object[] Synth(int index, double speed, long seed) { return (object[])_mSynth.Invoke(null, new object[] { index, speed, seed }); }

        public static double SpeedFor(int rate) { return Math.Max(0.5, Math.Min(2.0, 1.0 + rate * 0.1)); }

        public static string SynthToWav(string text, int rate, string path)
        {
            string error = EnsureLoaded();
            if (error != null) return error;
            return (string)_mWav.Invoke(null, new object[] { text, SpeedFor(rate), 42L, path });
        }

        // Short standalone playback for the panel's listen buttons.
        public static void Preview(string text, int rate)
        {
            StopPreview();
            if (EnsureLoaded() != null) return;
            lock (Gate) { _preview = new LocalSession(text, rate); }
        }

        public static bool IsPreviewPlaying
        {
            get { var p = _preview; return p != null && !p.FinishedPlayback && p.Error == null; }
        }

        public static void StopPreview()
        {
            LocalSession p;
            lock (Gate) { p = _preview; _preview = null; }
            if (p != null) p.Dispose();
        }
    }

    // Streams 48 kHz mono 16-bit audio through winmm waveOut with pause/resume and a sample-accurate position.
    sealed class WaveOutPlayer : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEFORMATEX { public short wFormatTag, nChannels; public int nSamplesPerSec, nAvgBytesPerSec; public short nBlockAlign, wBitsPerSample, cbSize; }
        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR { public IntPtr lpData; public int dwBufferLength, dwBytesRecorded; public IntPtr dwUser; public int dwFlags, dwLoops; public IntPtr lpNext, reserved; }
        [StructLayout(LayoutKind.Sequential)]
        struct MMTIME { public int wType; public uint cb; public uint pad; }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr handle, int device, ref WAVEFORMATEX format, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr handle, IntPtr header, int size);
        [DllImport("winmm.dll")] static extern int waveOutPause(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveOutRestart(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr handle);
        [DllImport("winmm.dll")] static extern int waveOutGetPosition(IntPtr handle, ref MMTIME time, int size);

        const int TIME_SAMPLES = 2, TIME_BYTES = 4;

        readonly object _gate = new object();
        IntPtr _handle;
        readonly int _rate;
        readonly List<GCHandle> _dataPins = new List<GCHandle>();
        readonly List<IntPtr> _headers = new List<IntPtr>();
        bool _open;

        public WaveOutPlayer(int rate)
        {
            _rate = rate;
            var format = new WAVEFORMATEX { wFormatTag = 1, nChannels = 1, nSamplesPerSec = rate, nAvgBytesPerSec = rate * 2, nBlockAlign = 2, wBitsPerSample = 16, cbSize = 0 };
            int result = waveOutOpen(out _handle, -1, ref format, IntPtr.Zero, IntPtr.Zero, 0);
            _open = result == 0;
            if (!_open) Logger.Log("waveOutOpen failed: " + result);
        }

        public void Write(short[] samples)
        {
            if (!_open || samples.Length == 0) return;
            lock (_gate)
            {
                if (!_open) return;
                var pin = GCHandle.Alloc(samples, GCHandleType.Pinned);
                _dataPins.Add(pin);
                var header = new WAVEHDR { lpData = pin.AddrOfPinnedObject(), dwBufferLength = samples.Length * 2 };
                IntPtr headerPtr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(WAVEHDR)));
                Marshal.StructureToPtr(header, headerPtr, false);
                _headers.Add(headerPtr);
                waveOutPrepareHeader(_handle, headerPtr, Marshal.SizeOf(typeof(WAVEHDR)));
                waveOutWrite(_handle, headerPtr, Marshal.SizeOf(typeof(WAVEHDR)));
            }
        }

        public void Pause() { if (_open) waveOutPause(_handle); }
        public void Resume() { if (_open) waveOutRestart(_handle); }

        public long PositionSamples
        {
            get
            {
                if (!_open) return 0;
                var time = new MMTIME { wType = TIME_SAMPLES };
                if (waveOutGetPosition(_handle, ref time, Marshal.SizeOf(typeof(MMTIME))) != 0) return 0;
                if (time.wType == TIME_SAMPLES) return time.cb;
                if (time.wType == TIME_BYTES) return time.cb / 2;
                return time.cb;
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (!_open) return;
                _open = false;
                try
                {
                    waveOutReset(_handle);
                    foreach (IntPtr header in _headers) { waveOutUnprepareHeader(_handle, header, Marshal.SizeOf(typeof(WAVEHDR))); Marshal.FreeHGlobal(header); }
                    waveOutClose(_handle);
                }
                catch { }
                foreach (var pin in _dataPins) { try { pin.Free(); } catch { } }
                _headers.Clear(); _dataPins.Clear();
            }
        }
    }

    // One playback of one text with the local voice: a worker synthesizes chunk by chunk ahead of playback
    // and a word timeline (original-text offsets, absolute sample ranges) drives the highlight.
    sealed class LocalSession : IDisposable
    {
        readonly object _gate = new object();
        readonly List<int> _wordStart = new List<int>();
        readonly List<int> _wordLen = new List<int>();
        readonly List<long> _sample0 = new List<long>();
        readonly List<long> _sample1 = new List<long>();
        WaveOutPlayer _player;
        Thread _worker;
        volatile bool _stop, _done;
        long _queued;
        public string Error { get; private set; }

        public LocalSession(string text, int rate)
        {
            double speed = LocalTrVoice.SpeedFor(rate);
            _worker = new Thread(delegate()
            {
                try
                {
                    int count = LocalTrVoice.Prepare(text, speed);
                    int sampleRate = LocalTrVoice.SampleRate;
                    _player = new WaveOutPlayer(sampleRate);
                    for (int i = 0; i < count && !_stop; i++)
                    {
                        object[] result = LocalTrVoice.Synth(i, speed, 42L);
                        var audio = (float[])result[0];
                        double pause = (double)result[1];
                        var starts = (int[])result[2];
                        var lens = (int[])result[3];
                        var t0 = (double[])result[4];
                        var t1 = (double[])result[5];
                        var pcm = new short[audio.Length];
                        for (int s = 0; s < audio.Length; s++)
                        {
                            float v = audio[s];
                            if (v > 1f) v = 1f; else if (v < -1f) v = -1f;
                            pcm[s] = (short)(v * 32767f);
                        }
                        lock (_gate)
                        {
                            for (int w = 0; w < starts.Length; w++)
                            {
                                _wordStart.Add(starts[w]);
                                _wordLen.Add(lens[w]);
                                _sample0.Add(_queued + (long)(t0[w] * sampleRate));
                                _sample1.Add(_queued + (long)(t1[w] * sampleRate));
                            }
                        }
                        if (_stop) break;
                        _player.Write(pcm);
                        Interlocked.Add(ref _queued, pcm.Length);
                        if (pause > 0)
                        {
                            var silence = new short[(int)(pause * sampleRate)];
                            _player.Write(silence);
                            Interlocked.Add(ref _queued, silence.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Error = ex.Message;
                    Logger.Log("ema session: " + ex.Message);
                }
                _done = true;
            });
            _worker.IsBackground = true;
            _worker.Start();
        }

        public bool StartedPlaying { get { var p = _player; return p != null && p.PositionSamples > 0; } }

        public bool FinishedPlayback
        {
            get
            {
                if (Error != null) return true;
                if (!_done) return false;
                var p = _player;
                return p == null || p.PositionSamples >= Interlocked.Read(ref _queued);
            }
        }

        public void Pause() { var p = _player; if (p != null) p.Pause(); }
        public void Resume() { var p = _player; if (p != null) p.Resume(); }

        // The word being heard right now; false when between words or before the first buffer.
        public bool CurrentWord(out int start, out int length)
        {
            start = 0; length = 0;
            var p = _player;
            if (p == null) return false;
            long pos = p.PositionSamples;
            lock (_gate)
            {
                for (int i = _sample0.Count - 1; i >= 0; i--)
                    if (pos >= _sample0[i])
                    {
                        if (pos <= _sample1[i] + 2400) { start = _wordStart[i]; length = _wordLen[i]; return true; }
                        return false;
                    }
            }
            return false;
        }

        public void Dispose()
        {
            _stop = true;
            var p = _player;
            if (p != null) p.Dispose();
            var w = _worker;
            if (w != null && w.IsAlive) { try { w.Join(1500); } catch { } }
        }
    }

    // Downloads and unpacks the voice package (one zip on GitHub Releases) into %LOCALAPPDATA%\SesliOkuma\ema.
    public sealed class EmaVoiceInstaller
    {
        readonly System.Windows.Forms.Control _ui;
        bool _busy;
        public event Action<int> Progress;
        public event Action Completed;
        public event Action<string> Failed;

        public EmaVoiceInstaller(System.Windows.Forms.Control ui) { _ui = ui; }

        void Post(Action a) { try { if (_ui.IsHandleCreated) _ui.BeginInvoke(a); else a(); } catch { } }

        public void Start()
        {
            if (_busy || LocalTrVoice.IsInstalled) return;
            _busy = true;
            string tmp = Path.Combine(Path.GetTempPath(), "SesliOkuma-TrVoice.zip");
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
            var wc = new WebClient();
            wc.Headers.Add("User-Agent", "SesliOkuma");
            wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e) { var h = Progress; if (h != null) Post(delegate { h(e.ProgressPercentage); }); };
            wc.DownloadFileCompleted += delegate(object s, AsyncCompletedEventArgs e)
            {
                string err = null;
                if (e.Error != null) err = e.Error.Message;
                else if (e.Cancelled) err = "cancelled";
                else { try { Extract(tmp); } catch (Exception ex) { err = ex.Message; } }
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                _busy = false;
                if (err != null) { Logger.Log("ema install failed: " + err); var hf = Failed; if (hf != null) Post(delegate { hf(err); }); }
                else { Logger.Log("ema voice installed"); LocalTrVoice.WarmUpAsync(); var hc = Completed; if (hc != null) Post(delegate { hc(); }); }
            };
            try { wc.DownloadFileAsync(new Uri(LocalTrVoice.PackageUrl), tmp); }
            catch (Exception ex) { _busy = false; var hf = Failed; if (hf != null) Post(delegate { hf(ex.Message); }); }
        }

        static void Extract(string zip)
        {
            string dir = LocalTrVoice.Dir;
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            ZipFile.ExtractToDirectory(zip, dir);
            if (!LocalTrVoice.IsInstalled) throw new FileNotFoundException("voice files missing after extraction");
        }
    }
}
