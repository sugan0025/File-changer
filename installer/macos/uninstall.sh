#!/bin/sh
set -eu

install_dir="$HOME/Applications/HOOT.app"
if [ -L "$install_dir" ] || [ ! -d "$install_dir" ]; then
    printf '%s\n' "No HOOT app bundle was found at $install_dir."
    exit 1
fi
resolved_parent=$(CDPATH= cd -- "$HOME/Applications" && pwd -P)
resolved_app=$(CDPATH= cd -- "$install_dir" && pwd -P)
if [ "$resolved_app" != "$resolved_parent/HOOT.app" ]; then
    printf '%s\n' 'Refusing to remove an app outside the expected Applications directory.'
    exit 1
fi
bundle_id=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$install_dir/Contents/Info.plist")
if [ "$bundle_id" != 'com.filee.app' ]; then
    printf '%s\n' 'The bundle is not HOOT.'
    exit 1
fi
"$install_dir/Contents/MacOS/Hoot" --uninstall-cleanup
if [ -L "$HOME/.local/bin/filee" ] && [ "$(readlink "$HOME/.local/bin/filee")" = "$install_dir/Contents/MacOS/filee-cli" ]; then
    rm -- "$HOME/.local/bin/filee"
fi
if [ -L "$HOME/.local/bin/hoot" ]; then
    hoot_target=$(readlink "$HOME/.local/bin/hoot")
    if [ "$hoot_target" = "$install_dir/Contents/MacOS/hoot-cli" ] || [ "$hoot_target" = "$install_dir/Contents/MacOS/filee-cli" ]; then
        rm -- "$HOME/.local/bin/hoot"
    fi
fi
rm -rf -- "$install_dir"
printf '%s\n' 'HOOT was removed. Your settings and conversion results were kept.'
