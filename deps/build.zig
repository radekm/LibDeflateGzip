const std = @import("std");

pub fn build(b: *std.build.Builder) void {
    const target = b.standardTargetOptions(.{});
    const mode = b.standardReleaseOptions();

    const lib = b.addSharedLibrary("deflate-gzip-native", null, .unversioned);
    lib.setTarget(target);
    lib.setBuildMode(mode);
    lib.linkLibC();
    lib.force_pic = true;
    lib.addIncludePath("libdeflate");
    lib.addIncludePath("libdeflate/lib");
    // All C files except 3 which are needed only for zlib format:
    // - `zlib_compress.c`,
    // - `zlib_decompress.c`,
    // - and `adler32.c`.
    lib.addCSourceFiles(&.{
        "libdeflate/lib/gzip_compress.c",
        "libdeflate/lib/gzip_decompress.c",
        "libdeflate/lib/deflate_compress.c",
        "libdeflate/lib/deflate_decompress.c",
        "libdeflate/lib/crc32.c",
        "libdeflate/lib/utils.c",
        "libdeflate/lib/arm/cpu_features.c",
        "libdeflate/lib/x86/cpu_features.c",
    }, &.{
        "-Wall",
    });
    lib.install();
}
