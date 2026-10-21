#!/bin/bash
set -euo pipefail
export LC_ALL=C SOURCE_DATE_EPOCH=1789680540
trap 'rc=$?; if [ "$rc" -ne 0 ] && [ -f /build/src/ffmpeg/ffbuild/config.log ]; then cp /build/src/ffmpeg/ffbuild/config.log /output/failed-config.log; fi' EXIT
export PREFIX=/opt/prefix
export PKG_CONFIG_LIBDIR="$PREFIX/lib/pkgconfig"
export PKG_CONFIG_PATH="$PKG_CONFIG_LIBDIR"
export PATH="$PREFIX/bin:$PATH"
JOBS="${JOBS:-6}"
mkdir -p /build/src "$PREFIX" /output/licenses
cd /sources/sources
sha256sum -c SHA256SUMS
cd /build/src
for name in ffmpeg x264 nv-codec-headers amf libvpl zlib mingw-w64; do
    mkdir -p "$name"
    archive=$(find /sources/sources -maxdepth 1 -name "$name-*.tar.*" -print -quit)
    tar -xf "$archive" -C "$name" --strip-components=1
    find "$name" -maxdepth 1 -type f \( -iname '*license*' -o -iname '*copying*' -o -iname '*copyright*' \) -exec sh -c 'cp "$1" "/output/licenses/$2-$(basename "$1").txt"' sh '{}' "$name" \;
done
# Update only Windows SDK interfaces, retaining the compiler's matching CRT headers.
cp /build/src/mingw-w64/mingw-w64-headers/include/windows.*.h /build/src/mingw-w64/mingw-w64-headers/include/rpcndr.h /usr/x86_64-w64-mingw32/include/
cd /build/src/zlib
CHOST=x86_64-w64-mingw32 CC=x86_64-w64-mingw32-gcc AR=x86_64-w64-mingw32-ar RANLIB=x86_64-w64-mingw32-ranlib ./configure --static --prefix="$PREFIX"
make -j"$JOBS"
make install
cd /build/src/x264
./configure --host=x86_64-w64-mingw32 --cross-prefix=x86_64-w64-mingw32- --prefix="$PREFIX" --disable-cli --enable-static --enable-pic --disable-lavf --disable-swscale
make -j"$JOBS"
make install
cd /build/src/nv-codec-headers
make PREFIX="$PREFIX" install
mkdir -p "$PREFIX/include/AMF"
cp -a /build/src/amf/amf/public/include/. "$PREFIX/include/AMF/"
cd /build/src/libvpl
# MinGW does not define _MSC_VER. Preserve the guard for actual MSVC builds.
sed -i 's/^#if _MSC_VER < 1400$/#if defined(_MSC_VER) \&\& _MSC_VER < 1400/' libvpl/src/windows/mfx_dispatcher_defs.h
cmake -S . -B build -G Ninja -DCMAKE_TOOLCHAIN_FILE=/sources/mingw-toolchain.cmake -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX="$PREFIX" -DCMAKE_INSTALL_LIBDIR=lib -DBUILD_DISPATCHER=ON -DBUILD_DEV=ON -DBUILD_PREVIEW=OFF -DBUILD_TOOLS=OFF -DBUILD_TOOLS_ONEVPL_EXPERIMENTAL=OFF -DINSTALL_EXAMPLE_CODE=OFF -DBUILD_SHARED_LIBS=OFF -DBUILD_TESTS=OFF
cmake --build build -j"$JOBS"
cmake --install build
printf '\nLibs.private: -lstdc++ -lwinpthread\n' >> "$PREFIX/lib/pkgconfig/vpl.pc"
cd /build/src/ffmpeg
# Recording-specific component allowlist. Keep the application and regression pipelines together.
./configure --prefix=/opt/ffmpeg --disable-everything --enable-protocol=file,pipe --enable-indev=gdigrab,lavfi --enable-demuxer=concat,matroska,mov,avi,wav,flac,aac,h264,hevc,m4v,image2,image2pipe,rawvideo --enable-muxer=matroska,mp4,mov,avi,wav,flac,adts,segment,stream_segment,image2,image2pipe,null,framemd5,framehash,hash,rawvideo --enable-encoder=libx264,libx264rgb,h264_nvenc,hevc_nvenc,h264_qsv,h264_amf,ffv1,mpeg4,mjpeg,png,flac,aac,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le,rawvideo,wrapped_avframe --enable-decoder=h264,hevc,ffv1,mpeg4,mjpeg,png,bmp,flac,aac,pcm_u8,pcm_s16le,pcm_s24le,pcm_s32le,pcm_f32le,pcm_f64le,rawvideo,wrapped_avframe --enable-parser=h264,hevc,mpeg4video,mjpeg,png,aac,flac --enable-bsf=h264_mp4toannexb,hevc_mp4toannexb,aac_adtstoasc,extract_extradata --enable-filter=ddagrab,gfxcapture,hwdownload,hwupload,format,aformat,scale,pad,fps,color,testsrc,testsrc2,sine,anullsrc,aresample,volume,apad,amix,anull,null,trim,atrim,setpts,asetpts,split,asplit,concat --extra-version=ScreenLossless --arch=x86_64 --target-os=mingw32 --cross-prefix=x86_64-w64-mingw32- --enable-cross-compile --pkg-config=pkg-config --pkg-config-flags=--static --disable-autodetect --enable-gpl --enable-version3 --enable-static --disable-shared --disable-debug --disable-doc --disable-ffplay --enable-libx264 --enable-libvpl --enable-amf --enable-ffnvcodec --enable-nvenc --enable-nvdec --enable-cuvid --enable-cuda --enable-filter=gfxcapture --enable-d3d11va --enable-d3d12va --enable-dxva2 --enable-zlib --disable-bzlib --disable-lzma --disable-iconv --enable-w32threads --disable-pthreads --extra-cflags="-I$PREFIX/include" --extra-ldflags="-L$PREFIX/lib -static -static-libgcc -static-libstdc++" --extra-libs="-lstdc++ -lwinpthread"
make -j"$JOBS"
x86_64-w64-mingw32-strip ffmpeg.exe ffprobe.exe
cp ffmpeg.exe ffprobe.exe /output/
cp config.h ffbuild/config.mak ffbuild/config.log /output/
dpkg-query -W > /output/toolchain-packages.txt
for package in gcc-mingw-w64-x86-64-posix g++-mingw-w64-x86-64-posix mingw-w64-common mingw-w64-x86-64-dev gcc-mingw-w64-base; do
    if [ -f "/usr/share/doc/$package/copyright" ]; then cp "/usr/share/doc/$package/copyright" "/output/licenses/$package-copyright.txt"; fi
done
cp /sources/sources.lock.json /output/
python /sources/record-build.py /output /sources
echo 'ScreenLossless FFmpeg build completed.'
