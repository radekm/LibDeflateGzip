Wrapper for gzip routines from libdeflate.

Native code supports following CPUs:
- x86-64 processors with AVX2 instructions.
- M1 processor on Mac with ARM.
- Cortex-A53 on Linux with ARM.

Windows with ARM is currently not supported.

# Building native libraries

1. Install Zig from commit `c0284e242`.
2. Go into `deps` directory and run `./rebuild-all-archs.sh` from there.
