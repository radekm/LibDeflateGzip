Wrapper for gzip routines from libdeflate.

Native code supports following CPUs:
- x86-64 processors with AVX2 instructions.
- M1 processor on Mac with ARM.
- Cortex-A53 on Linux with ARM.

Windows with ARM is currently not supported.

# Building native libraries

1. Install Zig from commit `05268bb96`.
2. Go into `deps` directory and run `./rebuild-all-archs.sh` from there.

# Updating libdeflate

Run

```shell
git subtree pull --prefix deps/libdeflate https://github.com/ebiggers/libdeflate.git <fill-sha> --squash
```
