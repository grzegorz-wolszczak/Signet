#!/usr/bin/env bash
# Packages the Signet application for linux-x64: an AppImage with WebKitGTK bundled, a .deb and an .rpm.
#
#   scripts/release/package-linux.sh <version> [<output folder>]
#
# Runs on Ubuntu 22.04 (the release pipeline uses the ubuntu:22.04 container): the AppImage takes WebKitGTK, GTK and
# their libraries from that system, so it runs on distributions with glibc 2.35 or newer. Needs, besides the .NET SDK:
#   libwebkit2gtk-4.1-0 libgtk-3-dev librsvg2-common file curl desktop-file-utils dpkg-dev rpm
# The default output folder is artifacts/release.
#
# Bundling WebKitGTK: libwebkit2gtk starts its helper processes (WebKitWebProcess, WebKitNetworkProcess) and loads its
# injected bundle from a directory compiled in as an absolute path. The bundled copy is patched to the relative path
# ././/lib/x86_64-linux-gnu/webkit2gtk-4.1 (same length), and AppRun runs the application with $APPDIR/usr as the
# working directory. The libraries the application P/Invokes by name are linked beside the executable, where .NET looks
# first, so the bundled ones load without LD_LIBRARY_PATH (which would leak into the programs Signet starts).
#
# The .deb and .rpm do not bundle WebKitGTK: they depend on the distribution's packages (installed with them).
set -euo pipefail

version="${1:?usage: package-linux.sh <version> [<output folder>]}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
output="${2:-$root/artifacts/release}"
mkdir -p "$output"
output="$(cd "$output" && pwd)"

app_id="io.github.grzegorz_wolszczak.Signet"
work="$root/artifacts/linux"
tools="$root/artifacts/tools"
publish="$work/publish"
packaging="$root/packaging/linux"
icons="$root/src/Signet.App/Assets/AppIcon"

# Pinned tool versions.
linuxdeploy_url="https://github.com/linuxdeploy/linuxdeploy/releases/download/1-alpha-20251107-1/linuxdeploy-x86_64.AppImage"
plugin_gtk_url="https://raw.githubusercontent.com/linuxdeploy/linuxdeploy-plugin-gtk/7a3fbc31a9e5075073ff8790f26effbac5f84453/linuxdeploy-plugin-gtk.sh"
appimagetool_url="https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-x86_64.AppImage"

# The libraries Signet and Avalonia P/Invoke by name; linked into the application folder of the AppImage.
pinvoked=(
    libwebkit2gtk-4.1.so.0 libjavascriptcoregtk-4.1.so.0 libgtk-3.so.0 libgdk-3.so.0
    libgobject-2.0.so.0 libglib-2.0.so.0 libgio-2.0.so.0 libcairo.so.2
)
webkit_dir="/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1"
webkit_dir_patched="././/lib/x86_64-linux-gnu/webkit2gtk-4.1"

# No FUSE in containers: the tool AppImages extract themselves.
export APPIMAGE_EXTRACT_AND_RUN=1

fetch() {
    # fetch <url> <file>: downloads once into the tools folder.
    [ -f "$2" ] || { curl -fsSL -o "$2.part" "$1" && mv "$2.part" "$2"; }
    chmod +x "$2"
}

rm -rf "$work"
mkdir -p "$work" "$tools"
fetch "$linuxdeploy_url" "$tools/linuxdeploy"
fetch "$plugin_gtk_url" "$tools/linuxdeploy-plugin-gtk.sh"
fetch "$appimagetool_url" "$tools/appimagetool"

echo "== Publishing Signet $version (linux-x64)"
dotnet publish "$root/src/Signet.App/Signet.App.csproj" -c Release -r linux-x64 --self-contained true \
    -p:SignetReleaseVersion="$version" -p:DebugType=none -p:PublishDocumentationFiles=false \
    -o "$publish" -nologo
rm -f "$publish"/*.pdb "$publish"/*.xml "$publish"/*.dbg
# The LTTng tracing provider of the runtime is optional (loaded only when liblttng-ust is there) and would make
# linuxdeploy fail on the missing library.
rm -f "$publish/libcoreclrtraceptprovider.so"
cp "$root/LICENSE" "$publish/LICENSE"

echo "== AppImage"
appdir="$work/Signet.AppDir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" "$appdir/usr/share/metainfo" \
    "$appdir/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1"
cp -a "$publish/." "$appdir/usr/bin/"
ln -s Signet.App "$appdir/usr/bin/signet"
cp "$packaging/signet.desktop" "$appdir/usr/share/applications/$app_id.desktop"
cp "$packaging/$app_id.metainfo.xml" "$appdir/usr/share/metainfo/$app_id.metainfo.xml"
cp "$icons/app_icon_256.png" "$work/$app_id.png"

# WebKit's helper processes and injected bundle (MiniBrowser is a demo program).
cp -a "$webkit_dir/WebKitWebProcess" "$webkit_dir/WebKitNetworkProcess" "$webkit_dir/injected-bundle" \
    "$appdir/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1/"
[ -e "$webkit_dir/WebKitGPUProcess" ] && cp -a "$webkit_dir/WebKitGPUProcess" "$appdir/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1/"

# linuxdeploy copies the libraries and their dependencies into usr/lib (except the ones every system has, see the
# AppImage excludelist) and points the RUNPATH of each ELF file at them; the gtk plugin adds GTK's modules, pixbuf
# loaders and schemas, and an AppRun hook that points GTK at them.
(
    # linuxdeploy finds its plugins on PATH.
    export PATH="$tools:$PATH"
    DEPLOY_GTK_VERSION=3 linuxdeploy --appdir "$appdir" \
        --library "/usr/lib/x86_64-linux-gnu/libwebkit2gtk-4.1.so.0" \
        --library "/usr/lib/x86_64-linux-gnu/libjavascriptcoregtk-4.1.so.0" \
        --deploy-deps-only "$appdir/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1" \
        --deploy-deps-only "$appdir/usr/lib/x86_64-linux-gnu/webkit2gtk-4.1/injected-bundle" \
        --desktop-file "$appdir/usr/share/applications/$app_id.desktop" \
        --icon-file "$work/$app_id.png" \
        --custom-apprun "$packaging/AppRun" \
        --plugin gtk
)

webkit_lib="$appdir/usr/lib/libwebkit2gtk-4.1.so.0"
[ -f "$webkit_lib" ] || { echo "linuxdeploy did not bundle libwebkit2gtk-4.1.so.0" >&2; exit 1; }
before="$(grep -c -a -F "$webkit_dir" "$webkit_lib" || true)"
sed -i "s|$webkit_dir|$webkit_dir_patched|g" "$webkit_lib"
if [ "$before" = 0 ] || grep -q -a -F "$webkit_dir" "$webkit_lib"; then
    echo "Patching the WebKit directory in libwebkit2gtk-4.1.so.0 failed" >&2
    exit 1
fi

for lib in "${pinvoked[@]}"; do
    if [ -e "$appdir/usr/lib/$lib" ]; then
        ln -sf "../lib/$lib" "$appdir/usr/bin/$lib"
    elif [ "$lib" = libwebkit2gtk-4.1.so.0 ] || [ "$lib" = libjavascriptcoregtk-4.1.so.0 ]; then
        echo "Missing bundled library: $lib" >&2
        exit 1
    fi
done

appimage="$output/Signet-$version-x86_64.AppImage"
rm -f "$appimage" "$appimage.zsync"
(
    cd "$output"
    ARCH=x86_64 "$tools/appimagetool" --no-appstream \
        -u "gh-releases-zsync|grzegorz-wolszczak|Signet|latest|Signet-*-x86_64.AppImage.zsync" \
        "$appdir" "$appimage"
)

echo "== deb / rpm"
# One file tree for both: the application in /opt/signet, the command signet in /usr/bin.
pkgroot="$work/pkgroot"
mkdir -p "$pkgroot/opt/signet" "$pkgroot/usr/bin" "$pkgroot/usr/share/applications" "$pkgroot/usr/share/metainfo"
cp -a "$publish/." "$pkgroot/opt/signet/"
ln -s /opt/signet/Signet.App "$pkgroot/usr/bin/signet"
cp "$packaging/signet.desktop" "$pkgroot/usr/share/applications/$app_id.desktop"
cp "$packaging/$app_id.metainfo.xml" "$pkgroot/usr/share/metainfo/$app_id.metainfo.xml"
for size in 256 512; do
    mkdir -p "$pkgroot/usr/share/icons/hicolor/${size}x${size}/apps"
    cp "$icons/app_icon_$size.png" "$pkgroot/usr/share/icons/hicolor/${size}x${size}/apps/$app_id.png"
done
mkdir -p "$pkgroot/usr/share/icons/hicolor/scalable/apps"
cp "$icons/app_icon_scalable.svg" "$pkgroot/usr/share/icons/hicolor/scalable/apps/$app_id.svg"
chmod -R u+rwX,go+rX,go-w "$pkgroot"

debroot="$work/deb"
cp -a "$pkgroot" "$debroot"
mkdir -p "$debroot/DEBIAN"
installed_size="$(du -sk "$debroot" | cut -f1)"
sed -e "s/@VERSION@/$version/" -e "s/@INSTALLED_SIZE@/$installed_size/" \
    "$packaging/deb/control" > "$debroot/DEBIAN/control"
dpkg-deb -Zxz --root-owner-group --build "$debroot" "$output/signet_${version}_amd64.deb"

rpmtop="$work/rpm"
mkdir -p "$rpmtop"
rpmbuild -bb --target x86_64 "$packaging/rpm/signet.spec" \
    --define "_topdir $rpmtop" --define "signet_version $version" --define "signet_root $pkgroot"
cp "$rpmtop/RPMS/x86_64/signet-$version-1.x86_64.rpm" "$output/"

echo "== Done"
ls -la "$output"
