#!/usr/bin/env bash
# The guard of the macOS release (design D3 of add-macos-packaging). Fails when the app bundle
#   - isn't signed with the project's certificate, so users would lose their grants with the update
#   - has a Mach-O file that isn't arm64 only, or needs a macOS newer than 14.0
#   - has a native Windows or Linux file (a PE file without a CLI header, or an ELF file)
#   - carries another version than the release
#
# Usage: assert-bundle.sh <bundle.app> <version> [--leaf <sha1> | --adhoc]
#   --leaf   the SHA-1 fingerprint of the expected signing certificate, for a test package signed with
#            a development identity (default: the project's certificate "Pisum Transcribe")
#   --adhoc  expect an ad hoc signature instead
set -euo pipefail

PROJECT_LEAF=a2eca9bd0a5157e33a43160ed01c502c6d86980b
MINIMUM_MACOS=14.0

usage() {
    echo "Usage: $0 <bundle.app> <version> [--leaf <sha1> | --adhoc]" >&2
    exit 2
}

[[ $# -ge 2 ]] || usage
bundle=$1
version=$2
shift 2
leaf=$PROJECT_LEAF
adhoc=false
while [[ $# -gt 0 ]]; do
    case $1 in
        --leaf)
            [[ $# -ge 2 ]] || usage
            leaf=$(tr '[:upper:]' '[:lower:]' <<<"${2//:/}")
            shift 2
            ;;
        --adhoc)
            adhoc=true
            shift
            ;;
        *) usage ;;
    esac
done
[[ -d $bundle ]] || { echo "Not a bundle: $bundle" >&2; exit 2; }

failures=()
fail() { failures+=("$1"); }

# 1. The signature, and the certificate it was made with.
if ! codesign --verify --strict "$bundle" 2>/dev/null; then
    fail "The signature doesn't verify: codesign --verify --strict \"$bundle\""
elif [[ $adhoc == true ]]; then
    details=$(codesign -dv "$bundle" 2>&1)
    grep -q '^Signature=adhoc$' <<<"$details" || fail "The bundle isn't signed ad hoc."
else
    requirement=$(codesign -d -r- "$bundle" 2>&1)
    if ! grep -qF "certificate leaf = H\"$leaf\"" <<<"$requirement"; then
        if grep -q 'cdhash' <<<"$requirement"; then
            fail "The bundle is signed ad hoc, not with the certificate $leaf."
        else
            fail "The bundle isn't signed with the certificate $leaf: ${requirement##*designated => }"
        fi
    fi
fi

# 2. Every file: its architecture and minimum macOS if it's Mach-O, and no native file of another platform.
# $1 <= $2 for versions like 14.0 or 10.15.
version_at_most() {
    awk -v a="$1" -v b="$2" 'BEGIN {
        n = split(a, x, "."); m = split(b, y, ".")
        for (i = 1; i <= (n > m ? n : m); i++) { if (x[i] + 0 < y[i] + 0) exit 0; if (x[i] + 0 > y[i] + 0) exit 1 }
        exit 0 }'
}

while IFS= read -r -d '' path; do
    name=${path#"$bundle"/}
    type=$(file -b "$path")
    case $type in
        Mach-O*)
            archs=$(lipo -archs "$path" 2>/dev/null || true)
            [[ $archs == arm64 ]] || fail "$name is built for '$archs', not arm64 only."
            # LC_BUILD_VERSION shows "minos", the older LC_VERSION_MIN_MACOSX "version".
            minos=$(vtool -arch arm64 -show-build "$path" 2>/dev/null |
                awk '$1 == "minos" || ($1 == "version" && prev ~ /LC_VERSION_MIN_MACOSX/) { print $2; exit } { prev = $0 }' ||
                true)
            if [[ -z $minos ]]; then
                fail "$name has no minimum macOS version."
            elif ! version_at_most "$minos" "$MINIMUM_MACOS"; then
                fail "$name needs macOS $minos, newer than $MINIMUM_MACOS."
            fi
            ;;
        PE32*)
            # .NET assemblies are PE files too; file names them by their CLI header.
            [[ $type == *"Mono/.Net assembly"* ]] || fail "$name is a native Windows file: $type"
            ;;
        ELF*)
            fail "$name is a native Linux file: $type"
            ;;
    esac
done < <(find "$bundle" -type f -print0)

# 3. The version.
bundle_version=$(/usr/libexec/PlistBuddy -c 'Print :CFBundleShortVersionString' "$bundle/Contents/Info.plist" 2>/dev/null || true)
[[ $bundle_version == "$version" ]] || fail "The bundle's version is '$bundle_version', not '$version'."

if [[ ${#failures[@]} -gt 0 ]]; then
    echo "The bundle $bundle fails the release checks:" >&2
    printf '  - %s\n' "${failures[@]}" >&2
    exit 1
fi
echo "The bundle $bundle passes the release checks (version $version)."
