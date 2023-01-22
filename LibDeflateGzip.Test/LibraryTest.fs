module LibDeflateGzip.Test

open System
open System.Buffers
open System.Diagnostics
open System.IO
open System.Text

open NUnit.Framework

let dataFile file = Path.Combine(__SOURCE_DIRECTORY__, "data", file)
let bufferSize = 1000

[<Test>]
let ``ungzip both members`` () =
    let compressed = File.ReadAllBytes(dataFile "ab.gz")
    use d = new Decompressor()

    let member1 = Array.create bufferSize 0uy
    let result, read, written = d.Decompress(compressed, member1)

    Assert.AreEqual(result, DecompressionResult.Success)
    Assert.AreEqual(FileInfo(dataFile "a.gz").Length, read)
    Assert.AreEqual(FileInfo(dataFile "a.txt").Length, written)
    let member1Expected = File.ReadAllBytes(dataFile "a.txt")
    CollectionAssert.AreEqual(member1Expected, member1.AsSpan().Slice(0, written).ToArray())

    let member2 = Array.create bufferSize 0uy
    let result, read, written = d.Decompress(compressed.AsSpan().Slice(read), member2)

    Assert.AreEqual(result, DecompressionResult.Success)
    Assert.AreEqual(FileInfo(dataFile "b.gz").Length, read)
    Assert.AreEqual(FileInfo(dataFile "b.txt").Length, written)
    let member2Expected = File.ReadAllBytes(dataFile "b.txt")
    CollectionAssert.AreEqual(member2Expected, member2.AsSpan().Slice(0, written).ToArray())

[<Test>]
let ``input buffer doesn't hold whole member`` () =
    let compressed = File.ReadAllBytes(dataFile "b.gz")
    let decompressed = Array.create bufferSize 0uy
    use d = new Decompressor()
    let result, read, written = d.Decompress(compressed.AsSpan().Slice(0, compressed.Length * 2 / 3), decompressed)

    Assert.AreEqual(DecompressionResult.BadData, result)
    Assert.AreEqual(0, read)
    Assert.AreEqual(0, written)

[<Test>]
let ``output buffer is not big enough`` () =
    let compressed = File.ReadAllBytes(dataFile "b.gz")
    let decompressed = Array.create (int (FileInfo(dataFile "b.txt").Length) - 5) 0uy
    use d = new Decompressor()
    let result, read, written = d.Decompress(compressed, decompressed)

    Assert.AreEqual(DecompressionResult.InsufficientSpace, result)
    Assert.AreEqual(0, read)
    Assert.AreEqual(0, written)

type Member = { Decompressed : Memory<byte>
                CompressedPos : int64
                CompressedLen : int }

/// Stream `compressed` must be open while using the sequence.
let membersFromStream (compressed : Stream) (maxCompressedMemberLen : int) (maxDecompressedMemberLen : int) = seq {
    // When using `use mutable inputOwner` compiler thinks that value is not mutable.
    let mutable inputOwner = MemoryPool.Shared.Rent(min (4 * 1024 * 1024) maxCompressedMemberLen)
    use _ = { new IDisposable with
                override _.Dispose() = inputOwner.Dispose() }
    let mutable inputMemory = inputOwner.Memory
    let mutable outputOwner = MemoryPool.Shared.Rent(min (32 * 1024 * 1024) maxDecompressedMemberLen)
    use _ = { new IDisposable with
                override _.Dispose() = outputOwner.Dispose() }
    let mutable outputMemory = outputOwner.Memory

    let mutable bytesInInputBuffer = 0
    let mutable eof = false

    let mutable pos = compressed.Position  // Measured from the beginning of the stream.

    use decompressor = new Decompressor()
    while not eof || bytesInInputBuffer > 0 do
        // Fill input buffer.
        while not eof &&  bytesInInputBuffer < inputMemory.Length do
            let n = compressed.Read(inputMemory.Span.Slice(bytesInInputBuffer))
            if n = 0
            then eof <- true
            else bytesInInputBuffer <- bytesInInputBuffer + n

        // Empty buffer is not valid gzip member.
        if bytesInInputBuffer > 0 then
            let result, read, written =
                decompressor.Decompress(inputMemory.Span.Slice(0, bytesInInputBuffer), outputMemory.Span)
            match result with
            | DecompressionResult.Success ->
                yield { Decompressed = outputMemory.Slice(0, written)
                        CompressedPos = pos
                        CompressedLen = read }

                bytesInInputBuffer <- bytesInInputBuffer - read
                pos <- pos + int64 read

                // Move unused input bytes to the beginning of input buffer.
                inputMemory.Span.Slice(read, bytesInInputBuffer).CopyTo(inputMemory.Span)
            | DecompressionResult.InsufficientSpace ->
                let n = min (2 * outputMemory.Length) maxDecompressedMemberLen
                if n > outputMemory.Length then
                    // Enlarge output buffer.
                    outputOwner.Dispose()
                    outputOwner <- MemoryPool.Shared.Rent(n)
                    outputMemory <- outputOwner.Memory
                else
                    failwith $"Output buffer len %d{outputMemory.Length} is too small"
            | DecompressionResult.BadData ->
                // If we're not at the end of stream it may help to enlarge
                // input buffer and load more compressed data.
                //
                // Note: When last call `readDataStr` reads remaining data from `compressed`
                // and fills `inBuffer` then `eof` is false even though we're at the end of file.
                // In this case member is corrupted but we don't detect it yet.
                // Instead we either enlarge `inBuffer` and detect corrupted member later,
                // or if enlarging fails we will raise exception stating two possible reasons
                // instead of saying that member is corrupted.
                if not eof then
                    let n = min (2 * inputMemory.Length) maxCompressedMemberLen
                    if n > inputMemory.Length then
                        // Enlarge input buffer. We need to preserve data in original buffer.
                        let newInputOwner = MemoryPool.Shared.Rent(n)
                        let newInputMemory = newInputOwner.Memory
                        inputMemory.Span.Slice(read, bytesInInputBuffer).CopyTo(newInputMemory.Span)
                        inputOwner.Dispose()
                        inputOwner <- newInputOwner
                        inputMemory <- newInputMemory
                    else
                        failwith
                            $"Input buffer len %d{inputMemory.Length} is too small or member at %d{pos} is corrupted"
                else
                    failwith $"Member at {pos} is corrupted"
            | _ ->
                failwith $"Decompressing member at %d{pos} failed with %A{result}"
}

// Simple benchmarking facility.
// Replace `dataFile "ab.gz"` with path to bigger file with more members.
// libdeflate should be faster than anything else (eg. faster than gzip).
[<Test>]
let ``iterate through members`` () =
    use s = new FileStream(dataFile "ab.gz", FileMode.Open)
    let mutable count = 0
    let sw = Stopwatch()
    sw.Start()
    for _ in membersFromStream s (8 * 1024 * 1024) (256 * 1024 * 1024) do
        count <- count + 1
    sw.Stop()
    printfn "Found %d members" count
    printfn "Elapsed millis %d" sw.ElapsedMilliseconds

[<Test>]
let ``gzip string`` () =
    let decompressed = Encoding.UTF8.GetBytes("SIX.MIXED.PIXIES.SIFT.SIXTY.PIXIE.DUST.BOXES")
    let compressed = Array.zeroCreate 1000
    let decompressedAgain = Array.zeroCreate 1000

    use compressor = new Compressor(9)
    let n = compressor.Compress(decompressed.AsSpan(), compressed.AsSpan())

    // Compression was successful.
    Assert.Greater(n, 0)

    use decompressor = new Decompressor()
    let result, read, written = decompressor.Decompress(compressed.AsSpan(0, n), decompressedAgain.AsSpan())

    Assert.AreEqual(DecompressionResult.Success, result)
    Assert.AreEqual(n, read)
    Assert.AreEqual(decompressed.Length, written)
    Assert.IsTrue(decompressed.AsSpan().SequenceEqual(decompressedAgain.AsSpan(0, written)))
