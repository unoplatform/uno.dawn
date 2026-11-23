#!/usr/bin/env bash
# Assembles an Ubuntu 22.04 (glibc 2.35) sysroot for building Dawn, without root: apt runs against its own state folder
# and the packages are unpacked, not installed. Building against it rather than the host keeps the glibc floor the same
# whatever the build image.
# Usage: make-sysroot.sh <dir> <x64|arm64>
set -euo pipefail
root=$(mkdir -p "$1" && cd "$1" && pwd)
case "$2" in
	x64) arch=amd64; mirror=http://archive.ubuntu.com/ubuntu ;;
	arm64) arch=arm64; mirror=http://ports.ubuntu.com/ubuntu-ports ;;
	*) echo "Unknown architecture: $2" >&2; exit 1 ;;
esac
apt_dir="$root/.apt"
mkdir -p "$apt_dir/etc/apt/sources.list.d" "$apt_dir/etc/apt/preferences.d" "$apt_dir/etc/apt/trusted.gpg.d" \
	"$apt_dir/var/lib/apt/lists/partial" "$apt_dir/var/cache/apt/archives/partial" "$apt_dir/var/lib/dpkg"
touch "$apt_dir/var/lib/dpkg/status"
cp /etc/apt/trusted.gpg.d/* "$apt_dir/etc/apt/trusted.gpg.d/" 2>/dev/null || true
cp /usr/share/keyrings/ubuntu-archive-keyring.gpg "$apt_dir/etc/apt/trusted.gpg.d/" 2>/dev/null || true
echo "deb [arch=$arch] $mirror jammy main universe" > "$apt_dir/etc/apt/sources.list"
echo "deb [arch=$arch] $mirror jammy-updates main universe" >> "$apt_dir/etc/apt/sources.list"
apt_opts=(-o "Dir=$apt_dir" -o "APT::Architecture=$arch" -o "APT::Architectures=$arch" -o "Debug::NoLocking=1")
apt-get "${apt_opts[@]}" update -qq
# The C/C++ runtime and kernel headers to compile and link against, and the X11 headers Dawn's Vulkan surface uses.
packages="libc6 libc6-dev linux-libc-dev libgcc-s1 libgcc-12-dev libstdc++6 libstdc++-12-dev
	libx11-dev x11proto-dev libxcb1-dev libxau-dev libxdmcp-dev libx11-xcb-dev libxrandr-dev libxrender-dev libxext-dev
	libxinerama-dev libxcursor-dev libxi-dev libxfixes-dev"
(cd "$apt_dir/var/cache/apt/archives" && apt-get "${apt_opts[@]}" download $packages)
for deb in "$apt_dir"/var/cache/apt/archives/*.deb; do
	dpkg-deb -x "$deb" "$root"
done
# Absolute symlinks inside the packages point at the host; make them relative to the sysroot.
find "$root" -type l | while read -r link; do
	target=$(readlink "$link")
	case "$target" in /*) ln -sfn "$root$target" "$link" ;; esac
done
echo "Sysroot in $root"
