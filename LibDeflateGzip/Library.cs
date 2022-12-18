namespace LibDeflateGzip;

using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

internal class DecompressorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public DecompressorHandle() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        Native.FreeDecompressor(handle);
        return true;
    }
}

public enum DecompressionResult
{
    Success = 0,
    BadData = 1,
    InsufficientSpace = 3,
}

internal partial class Native
{
    private const string DllName = "deflate-gzip-native";

    [LibraryImport(DllName, EntryPoint = "libdeflate_alloc_decompressor")]
    internal static partial DecompressorHandle AllocDecompressor();
    //
    // struct libdeflate_decompressor *decompressor,
    // const void *in, size_t in_nbytes,
    // void *out, size_t out_nbytes_avail,
    //     size_t *actual_in_nbytes_ret,
    // size_t *actual_out_nbytes_ret

    [LibraryImport(DllName, EntryPoint = "libdeflate_gzip_decompress_ex")]
    internal static partial DecompressionResult Decompress(
        DecompressorHandle handle,
        nint input, nuint inputSize,
        nint output, nuint outputSize,
        out nuint read, out nuint written
    );

    [LibraryImport(DllName, EntryPoint = "libdeflate_free_decompressor")]
    internal static partial void FreeDecompressor(nint decompressor);
}

public sealed class Decompressor : IDisposable
{
    private DecompressorHandle handle;

    public Decompressor()
    {
        handle = Native.AllocDecompressor();
    }

    public unsafe DecompressionResult Decompress(ReadOnlySpan<byte> input, Span<byte> output, out int read,
        out int written)
    {
        fixed (byte* inputPtr = input)
        fixed (byte* outputPtr = output)
        {
            var result = Native.Decompress(
                handle,
                (nint)inputPtr, (nuint)input.Length,
                (nint)outputPtr, (nuint)output.Length,
                out var readRaw, out var writtenRaw);
            read = (int)readRaw;
            written = (int)writtenRaw;
            return result;
        }
    }

    public void Dispose()
    {
        handle.Dispose();
    }
}
