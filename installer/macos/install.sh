#!/bin/sh
set -eu

source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd -P)
install_dir="$HOME/Applications/Filee.app"
source_app="$source_dir/HOOT.app"
[ -d "$source_app" ] || source_app="$source_dir/Filee.app"
bin_dir="$HOME/.local/bin"
if [ -e "$install_dir" ] || [ -L "$install_dir" ]; then
    printf '%s\n' "An installation already exists at $install_dir. Run uninstall.sh first."
    exit 1
fi
# A filee link left by an earlier Filee whose app is gone (moved to the Trash instead of uninstall.sh) is replaced.
# Anything else at that path isn't ours and stays.
stale_link=false
if [ -L "$bin_dir/filee" ] && [ ! -e "$bin_dir/filee" ]; then
    case $(readlink "$bin_dir/filee") in
        */Filee.app/Contents/MacOS/filee-cli) stale_link=true ;;
    esac
fi
if [ "$stale_link" = false ] && { [ -e "$bin_dir/filee" ] || [ -L "$bin_dir/filee" ]; }; then
    printf '%s\n' "$bin_dir/filee already exists. Remove or rename it before installing Filee."
    exit 1
fi
if [ -e "$bin_dir/hoot" ] || [ -L "$bin_dir/hoot" ]; then
    printf '%s\n' "$bin_dir/hoot already exists. Remove or rename it before installing HOOT."
    exit 1
fi
test -x "$source_app/Contents/MacOS/Filee"
mkdir -p "$HOME/Applications" "$bin_dir"
/usr/bin/ditto "$source_app" "$install_dir"
# ditto keeps the download's quarantine flag: macOS would then start Filee from a translocated copy and could stop
# the filee command without a word (#316).
/usr/bin/xattr -dr com.apple.quarantine "$install_dir" 2>/dev/null || true
if [ "$stale_link" = true ]; then
    rm -f -- "$bin_dir/filee"
    printf '%s\n' "Replaced the filee link left by an earlier installation."
fi
ln -s "$install_dir/Contents/MacOS/filee-cli" "$bin_dir/filee"
hoot_cli="$install_dir/Contents/MacOS/hoot-cli"
[ -x "$hoot_cli" ] || hoot_cli="$install_dir/Contents/MacOS/filee-cli"
ln -s "$hoot_cli" "$bin_dir/hoot"
printf '%s\n' "Installed HOOT at $install_dir" "CLI: $bin_dir/hoot (legacy alias: $bin_dir/filee). Add $bin_dir to PATH if needed."
printf '%s\n' 'Open HOOT from Applications. Grant Accessibility and Finder Automation access when requested.'
printf '%s\n' 'If HOOT is already listed and switched on under Accessibility, remove it with the - button and add it again with +.'
