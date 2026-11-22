# Built by scripts/release/package-linux.sh from an already published file tree (signet_root); nothing is compiled.
Name:           signet
Version:        %{signet_version}
Release:        1
Summary:        Editor for EPUB 2 and EPUB 3 ebooks
License:        GPL-3.0-or-later
URL:            https://github.com/grzegorz-wolszczak/Signet
BuildArch:      x86_64
# The self-contained .NET files would yield bogus automatic requirements; the real ones are listed by soname, which
# Fedora, openSUSE and others resolve to their own WebKitGTK and GTK packages.
AutoReqProv:    no
Requires:       libwebkit2gtk-4.1.so.0()(64bit)
Requires:       libgtk-3.so.0()(64bit)

# No stripping or other post-processing of the prebuilt .NET files.
%global debug_package %{nil}
%global __os_install_post %{nil}

%description
Code editor with preview, find and replace, table of contents, metadata, reports, spellcheck, reformatting and a book
check based on EPUBCheck.

%install
cp -a %{signet_root}/. %{buildroot}/

%files
/opt/signet
/usr/bin/signet
/usr/share/applications/io.github.grzegorz_wolszczak.Signet.desktop
/usr/share/metainfo/io.github.grzegorz_wolszczak.Signet.metainfo.xml
/usr/share/icons/hicolor/256x256/apps/io.github.grzegorz_wolszczak.Signet.png
/usr/share/icons/hicolor/512x512/apps/io.github.grzegorz_wolszczak.Signet.png
/usr/share/icons/hicolor/scalable/apps/io.github.grzegorz_wolszczak.Signet.svg
