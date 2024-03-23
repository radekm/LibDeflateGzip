const std = @import("std");

pub fn build(b: *std.Build) void {
    const target = b.standardTargetOptions(.{});
    const optimize = b.standardOptimizeOption(.{});

    const lib = b.addSharedLibrary(.{
        .name = "deflate-gzip-native",
        .target = target,
        .optimize = optimize,
        .pic = true,
    });
    lib.addIncludePath(.{ .path = "libdeflate" });
    lib.addIncludePath(.{ .path = "libdeflate/lib" });
    // All C files except 3 which are needed only for zlib format:
    // - `zlib_compress.c`,
    // - `zlib_decompress.c`,
    // - and `adler32.c`.
    lib.addCSourceFiles(.{
        .files = &.{
            "libdeflate/lib/gzip_compress.c",
            "libdeflate/lib/gzip_decompress.c",
            "libdeflate/lib/deflate_compress.c",
            "libdeflate/lib/deflate_decompress.c",
            "libdeflate/lib/crc32.c",
            "libdeflate/lib/utils.c",
            "libdeflate/lib/arm/cpu_features.c",
            "libdeflate/lib/x86/cpu_features.c",
        },
        .flags = &.{"-Wall"},
    });
    lib.linkLibC();

    b.installArtifact(lib);
}
