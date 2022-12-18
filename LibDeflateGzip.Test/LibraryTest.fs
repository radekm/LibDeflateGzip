module LibDeflateGzip.Test

open System
open System.IO

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
