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

    rm -rf .zig-cache zig-out

    zig build -Doptimize=ReleaseFast -Dtarget="$target" -Dcpu="$cpu"

    dest="$runtimes_dir/$net_arch/native"
    mkdir -p "$dest"
    mv "zig-out/$file" "$dest"
}

rm -rf "$runtimes_dir"
mkdir "$runtimes_dir"

compile x86_64-linux-gnu   haswell linux-x64 lib/libdeflate-gzip-native.so
compile x86_64-macos       haswell osx-x64   lib/libdeflate-gzip-native.dylib
compile x86_64-windows-gnu haswell win-x64   bin/deflate-gzip-native.dll

compile aarch64-linux-gnu cortex_a53 linux-arm64 lib/libdeflate-gzip-native.so
compile aarch64-macos     apple_m1   osx-arm64   lib/libdeflate-gzip-native.dylib
