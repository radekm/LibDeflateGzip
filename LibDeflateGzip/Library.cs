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

internal class CompressorHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    public CompressorHandle() : base(true)
    {
    }

    protected override bool ReleaseHandle()
    {
        Native.FreeCompressor(handle);
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

    [LibraryImport(DllName, EntryPoint = "libdeflate_gzip_decompress_ex")]
    internal static partial DecompressionResult Decompress(
        DecompressorHandle handle,
        nint input, nuint inputSize,
        nint output, nuint outputSize,
        out nuint read, out nuint written
    );

    [LibraryImport(DllName, EntryPoint = "libdeflate_free_decompressor")]
    internal static partial void FreeDecompressor(nint decompressor);

    [LibraryImport(DllName, EntryPoint = "libdeflate_alloc_compressor")]
    internal static partial CompressorHandle AllocCompressor(int compressionLevel);

    [LibraryImport(DllName, EntryPoint = "libdeflate_gzip_compress")]
    internal static partial nuint Compress(
        CompressorHandle handle,
        nint input, nuint inputSize,
        nint output, nuint outputSize
    );

    [LibraryImport(DllName, EntryPoint = "libdeflate_free_compressor")]
    internal static partial void FreeCompressor(nint decompressor);
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

public sealed class Compressor : IDisposable
{
    private CompressorHandle handle;

    public Compressor(int compressionLevel)
    {
        handle = Native.AllocCompressor(compressionLevel);
    }

    public unsafe int Compress(ReadOnlySpan<byte> input, Span<byte> output)
    {
        fixed (byte* inputPtr = input)
        fixed (byte* outputPtr = output)
        {
            var result = Native.Compress(
                handle,
                (nint)inputPtr, (nuint)input.Length,
                (nint)outputPtr, (nuint)output.Length);
            return (int)result;
        }
    }

    public void Dispose()
    {
        handle.Dispose();
    }
}
