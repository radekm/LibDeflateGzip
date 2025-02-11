using System.Buffers;

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

public sealed class BufferedDecompressor : IDisposable
{
    private bool disposed;
    private readonly int maxBufferLen;
    private IMemoryOwner<byte>? bufferOwner;
    private Memory<byte> buffer;
    private int lastWritten;
    private readonly Decompressor decompressor;

    public BufferedDecompressor(int initialBufferLen, int maxBufferLen)
    {
        if (initialBufferLen <= 0) throw new ArgumentOutOfRangeException(nameof(initialBufferLen));
        if (maxBufferLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxBufferLen));
        if (initialBufferLen > maxBufferLen) throw new ArgumentOutOfRangeException(nameof(initialBufferLen));

        this.maxBufferLen = maxBufferLen;

        // Must be called after setting `maxBufferLen`.
        EnsureBufferLength(Math.Min(Math.Max(initialBufferLen, 4096), maxBufferLen));

        decompressor = new Decompressor();
    }

    private void EnsureBufferLength(int desiredLength)
    {
        if (desiredLength > maxBufferLen)
            throw new ArgumentOutOfRangeException(nameof(desiredLength));

        var currentLength = bufferOwner == null ? 0 : buffer.Length;

        // We need to enlarge buffer.
        if (currentLength < desiredLength)
        {
            var newBufferOwner = MemoryPool<byte>.Shared.Rent(desiredLength);
            using var origBufferOwner = bufferOwner;  // This is automatically released.

            bufferOwner = newBufferOwner;
            buffer = newBufferOwner.Memory;
        }
    }

    /// <summary>
    /// Decompresses `input` into internal buffer.
    /// If `DecompressionResult.Success` is returned
    /// then decompressed data are available via `DecompressedData` property.
    /// `DecompressedData` is valid till the next call of `Decompress`.
    /// </summary>
    public DecompressionResult Decompress(ReadOnlySpan<byte> input, out int read)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(BufferedDecompressor));

        // Ensure that `buffer` is big enough for decompressed data.
        EnsureBufferLength((int)Math.Min((uint)input.Length * 2, (uint)maxBufferLen));

        while (true)
        {
            // We don't give `Decompress` more than `maxBufferLen` bytes of `buffer`.
            var output = buffer.Length > maxBufferLen ? buffer.Span[..maxBufferLen] : buffer.Span;
            switch (decompressor.Decompress(input, output, out read, out lastWritten))
            {
                case DecompressionResult.Success:
                    return DecompressionResult.Success;
                case DecompressionResult.BadData:
                    return DecompressionResult.BadData;
                case DecompressionResult.InsufficientSpace:
                    // Buffer has maximum length and it is not big enough.
                    if (buffer.Length >= maxBufferLen)
                        return DecompressionResult.InsufficientSpace;

                    var newLength = (int)Math.Min((uint)buffer.Length * 2, (uint)maxBufferLen);
                    EnsureBufferLength(newLength);

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    /// <summary>
    /// If the last call of `Decompress` returned `DecompressionResult.Success`
    /// then this property returns decompressed data.
    /// The content of the property is valid till the next call of `Decompress`.
    ///
    /// Note that `Decompress` may even reallocate internal buffer so
    /// the old reference may point to deallocated old buffer.
    /// </summary>
    public Memory<byte> DecompressedData => buffer[..lastWritten];

    public void Dispose()
    {
        disposed = true;
        decompressor.Dispose();
        buffer = Memory<byte>.Empty;
        bufferOwner?.Dispose();
        bufferOwner = null;
        lastWritten = 0;
    }
}

public sealed class BufferedCompressor : IDisposable
{
    private bool disposed;
    private readonly int maxBufferLen;
    private IMemoryOwner<byte>? bufferOwner;
    private Memory<byte> buffer;
    private int lastWritten;
    private readonly Compressor compressor;

    public BufferedCompressor(int compressionLevel, int initialBufferLen, int maxBufferLen)
    {
        if (initialBufferLen <= 0) throw new ArgumentOutOfRangeException(nameof(initialBufferLen));
        if (maxBufferLen <= 0) throw new ArgumentOutOfRangeException(nameof(maxBufferLen));
        if (initialBufferLen > maxBufferLen) throw new ArgumentOutOfRangeException(nameof(initialBufferLen));

        this.maxBufferLen = maxBufferLen;

        // Must be called after setting `maxBufferLen`.
        EnsureBufferLength(Math.Min(Math.Max(initialBufferLen, 4096), maxBufferLen));

        compressor = new Compressor(compressionLevel);
    }

    private void EnsureBufferLength(int desiredLength)
    {
        if (desiredLength > maxBufferLen)
            throw new ArgumentOutOfRangeException(nameof(desiredLength));

        var currentLength = bufferOwner == null ? 0 : buffer.Length;

        // We need to enlarge buffer.
        if (currentLength < desiredLength)
        {
            var newBufferOwner = MemoryPool<byte>.Shared.Rent(desiredLength);
            using var origBufferOwner = bufferOwner;  // This is automatically released.

            bufferOwner = newBufferOwner;
            buffer = newBufferOwner.Memory;
        }
    }

    /// <summary>
    /// Compresses `input` into internal buffer.
    /// If `true` is returned
    /// then compressed data are available via `CompressedData` property.
    /// `CompressedData` is valid till the next call of `Compress`.
    /// </summary>
    public bool Compress(ReadOnlySpan<byte> input)
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(BufferedCompressor));

        // Ensure that `buffer` is big enough for compressed data.
        EnsureBufferLength((int)Math.Min((uint)input.Length * 2, (uint)maxBufferLen));

        while (true)
        {
            // We don't give `Compress` more than `maxBufferLen` bytes of `buffer`.
            var output = buffer.Length > maxBufferLen ? buffer.Span[..maxBufferLen] : buffer.Span;
            lastWritten = compressor.Compress(input, output);

            if (lastWritten > 0) return true;

            // Buffer has maximum length and it is not big enough.
            if (buffer.Length >= maxBufferLen) return false;

            // Make buffer bigger and try compression again.
            var newLength = (int)Math.Min((uint)buffer.Length * 2, (uint)maxBufferLen);
            EnsureBufferLength(newLength);
        }
    }

    /// <summary>
    /// If the last call of `Compress` returned `true`
    /// then this property returns compressed data.
    /// The content of the property is valid till the next call of `Compress`.
    ///
    /// Note that `Compress` may even reallocate internal buffer so
    /// the old reference may point to deallocated old buffer.
    /// </summary>
    public Memory<byte> CompressedData => buffer[..lastWritten];

    public void Dispose()
    {
        disposed = true;
        compressor.Dispose();
        buffer = Memory<byte>.Empty;
        bufferOwner?.Dispose();
        bufferOwner = null;
        lastWritten = 0;
    }
}
