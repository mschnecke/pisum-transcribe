#!/usr/bin/env bash
# Builds the macOS release package artifacts/Pisum.Transcribe_<version>_osx-arm64.pkg: publishes the app bundle,
# runs the guard on it and wraps it with pkgbuild and productbuild, unsigned. The one command that turns a checkout
# into the package, on a developer's Mac and on the runner (design D1 and D4 of add-macos-packaging).
#
# Usage: build-pkg.sh <version> [--identity <name>] [--leaf <sha1> | --adhoc]
#   <version>    the release version, without a leading 'v', optionally with a pre-release suffix (1.4.0-rc.1)
#   --identity   the signing identity of the app (default: the project's certificate "Pisum Transcribe")
#   --leaf       the SHA-1 fingerprint the guard expects, for a test package signed with a development identity
#   --adhoc      sign ad hoc, and let the guard expect that; the package then loses its grants on every update
set -euo pipefail

usage() {
    echo "Usage: $0 <version> [--identity <name>] [--leaf <sha1> | --adhoc]" >&2
    exit 2
}

[[ $# -ge 1 && $1 != -* ]] || usage
version=$1
shift
identity='Pisum Transcribe'
guard_args=()
while [[ $# -gt 0 ]]; do
    case $1 in
        --identity)
            [[ $# -ge 2 ]] || usage
            identity=$2
            shift 2
            ;;
        --leaf)
            [[ $# -ge 2 ]] || usage
            guard_args=(--leaf "$2")
            shift 2
            ;;
        --adhoc)
            identity=-
            guard_args=(--adhoc)
            shift
            ;;
        *) usage ;;
    esac
done

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
root=$(cd "$script_dir/../.." && pwd)
identifier=io.github.mschnecke.pisum-transcribe
# The publish output is kept beside this script, where .gitignore's publish/ keeps it untracked.
publish_dir=$script_dir/publish
bundle="$publish_dir/Pisum Transcribe.app"
output_dir=$root/artifacts
package=$output_dir/Pisum.Transcribe_${version}_osx-arm64.pkg

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
rm -rf "$publish_dir"
rm -f "$package"
mkdir -p "$output_dir"

# 1. The release bundle, which the project's bundle target assembles and signs after the publish (D1). The same flags
# as the MSI: the native package ships three LICENSE files that NuGet flattens onto one path, which publish refuses
# unless told not to; the bundle target removes that LICENSE and puts the project's into Contents/Resources.
dotnet publish "$root/src/Pisum.Transcribe" \
    --configuration Release \
    --framework net10.0 \
    --runtime osx-arm64 \
    --self-contained true \
    -p:PublishReadyToRun=true \
    -p:PublishDocumentationFile=false \
    -p:ErrorOnDuplicatePublishOutputFiles=false \
    -p:Version="$version" \
    -p:PisumCodesignIdentity="$identity" \
    --output "$publish_dir"

# 2. The guard (D3).
"$script_dir/assert-bundle.sh" "$bundle" "$version" ${guard_args[@]+"${guard_args[@]}"}

# 3. The component: the bundle into /Applications, never relocated to another copy with the same identifier. Only
# the two scripts go into the package, not the rest of this folder.
mkdir -p "$work/root" "$work/scripts" "$work/packages"
cp -R "$bundle" "$work/root/"
cp "$script_dir/preinstall" "$script_dir/postinstall" "$work/scripts/"
chmod +x "$work/scripts/preinstall" "$work/scripts/postinstall"
pkgbuild --analyze --root "$work/root" "$work/component.plist"
plutil -replace 0.BundleIsRelocatable -bool false "$work/component.plist"
plutil -replace 0.BundleHasStrictIdentifier -bool true "$work/component.plist"
plutil -replace 0.BundleIsVersionChecked -bool false "$work/component.plist"
plutil -replace 0.BundleOverwriteAction -string upgrade "$work/component.plist"
pkgbuild \
    --root "$work/root" \
    --component-plist "$work/component.plist" \
    --install-location /Applications \
    --identifier "$identifier" \
    --version "$version" \
    --scripts "$work/scripts" \
    "$work/packages/PisumTranscribe.pkg"

# 4. The product, unsigned, with the distribution's checks: Apple silicon, macOS 14, no older version over a newer one.
sed "s/@VERSION@/$version/g" "$script_dir/Distribution.xml" >"$work/Distribution.xml"
productbuild \
    --distribution "$work/Distribution.xml" \
    --package-path "$work/packages" \
    "$package"

echo "Created: $package"
echo "  Version: $version"
echo "  Signed:  $identity"
echo "  Bundle:  $(du -sh "$bundle" | cut -f1)"
echo "  Package: $(du -h "$package" | cut -f1)"
