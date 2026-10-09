using System;
using System.Runtime.InteropServices;
using System.Text;

// net48 shims for the vendored EmaLightning.Onnx sources.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace EmaLightning.Onnx
{
    internal static class Utf8Ptr
    {
        public static string? ToString(IntPtr ptr)
        {
            if (ptr == IntPtr.Zero)
                return null;
            var length = 0;
            while (Marshal.ReadByte(ptr, length) != 0)
                length++;
            var bytes = new byte[length];
            Marshal.Copy(ptr, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
    }
}
