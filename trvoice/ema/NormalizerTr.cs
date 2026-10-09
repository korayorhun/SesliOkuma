using System.Runtime.InteropServices;
using System.Text;

namespace EmaLightning.Onnx;

// normalizer-tr (Erdem Tuna, Apache-2.0) through its C shim normalizer_tr.dll (ffi/normalizer-tr-ffi): the fallback policy
// reads every notation one way or another, so nothing is dropped before the model. One handle, used from one thread.
public sealed class NormalizerTr : INormalizer, IDisposable
{
    private const int FallbackPolicy = 2;
    private IntPtr _handle;

    private NormalizerTr(IntPtr handle) => _handle = handle;

    // The crate versions the shim was built from; null when the DLL is not here.
    public static string? Version
    {
        get
        {
            try { return Utf8Ptr.ToString(Native.ntr_version()); }
            catch (Exception ex) when (IsLoadFailure(ex)) { return null; }
        }
    }

    // Null with the reason when the DLL is missing, of the wrong build, or its built-in resources fail validation.
    public static NormalizerTr? TryCreate(out string? error)
    {
        try
        {
            var handle = Native.ntr_new();
            if (handle == IntPtr.Zero)
            {
                error = "normalizer-tr did not initialise";
                return null;
            }
            error = null;
            return new NormalizerTr(handle);
        }
        catch (Exception ex) when (IsLoadFailure(ex))
        {
            error = ex.GetType().Name + ": " + ex.Message;
            return null;
        }
    }

    public string? Normalize(string text)
    {
        if (_handle == IntPtr.Zero || text.Length == 0)
            return null;
        var utf8 = Encoding.UTF8.GetBytes(text + "\0");
        var result = Native.ntr_normalize(_handle, utf8, FallbackPolicy, out _);
        if (result == IntPtr.Zero)
            return null;
        try { return Utf8Ptr.ToString(result); }
        finally { Native.ntr_free_string(result); }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero)
            return;
        Native.ntr_free(_handle);
        _handle = IntPtr.Zero;
    }

    private static bool IsLoadFailure(Exception ex) => ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException;

    private static class Native
    {
        private const string Library = "normalizer_tr";

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ntr_new();

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ntr_free(IntPtr handle);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ntr_normalize(IntPtr handle, byte[] utf8NullTerminated, int policy, out int complete);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern void ntr_free_string(IntPtr text);

        [DllImport(Library, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr ntr_version();
    }
}
