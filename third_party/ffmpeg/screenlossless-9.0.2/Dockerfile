FROM python@sha256:423ed6ab25b1921a477529254bfeeabf5855151dc2c3141699a1bfc852199fbf
RUN apt-get update && apt-get install -y --no-install-recommends gcc-mingw-w64-x86-64-posix g++-mingw-w64-x86-64-posix make nasm pkg-config cmake ninja-build autoconf automake libtool xz-utils ca-certificates && rm -rf /var/lib/apt/lists/*
WORKDIR /build
