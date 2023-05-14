#!/bin/sh

set -e

runtimes_dir=../LibDeflateGzip/runtimes

compile()
{
    target="$1"
    cpu="$2"
    net_arch="$3"
    file="$4"

    echo "----------"
    echo "Compiling: target $target, CPU $cpu, .NET arch $net_arch, file $file"

    rm -rf zig-cached zig-out

    zig build -Doptimize=ReleaseFast -Dtarget="$target" -Dcpu="$cpu"

    dest="$runtimes_dir/$net_arch/native"
    mkdir -p "$dest"
    mv "zig-out/lib/$file" "$dest"
}

rm -rf "$runtimes_dir"
mkdir "$runtimes_dir"

compile x86_64-linux-gnu   core_avx2 linux-x64 libdeflate-gzip-native.so
compile x86_64-macos       core_avx2 osx-x64   libdeflate-gzip-native.dylib
compile x86_64-windows-gnu core_avx2 win-x64   deflate-gzip-native.dll

compile aarch64-linux-gnu cortex_a53 linux-arm64 libdeflate-gzip-native.so
compile aarch64-macos     apple_m1   osx-arm64   libdeflate-gzip-native.dylib
