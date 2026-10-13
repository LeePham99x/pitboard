#!/bin/sh
# Takes the documentation's pictures of the menu and the window from the debug build in the
# twoTools fixture, with the UI test ScreenshotTests, and writes them at the sizes the docs
# use. Needs what the UI tests need: build-xcframework.sh --fixture, then xcodegen. The
# pictures take this Mac's appearance.
set -eu

cd "$(dirname "$0")/../../.."
build=apps/macos/build
debug=$build/UITestData/Build/Products/Debug
results=$build/Screenshots.xcresult
attachments=$(mktemp -d)
lsregister=/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister

# Building registers the debug build's scheme and Share extension with macOS until undone.
unregister() {
    for appex in "$debug/PitboardShare.appex" \
        "$debug/Pitboard.app/Contents/PlugIns/PitboardShare.appex"; do
        pluginkit -r "$appex" 2>/dev/null || true
    done
    for app in "$debug"/Pitboard.app/Contents/Frameworks/Sparkle.framework/Versions/*/Updater.app \
        "$debug/Pitboard.app" "$debug/PitboardUITests-Runner.app"; do
        if [ -d "$app" ]; then "$lsregister" -u "$app" 2>/dev/null || true; fi
    done
    rm -rf "$attachments"
}
trap unregister EXIT

rm -rf "$results"
xcodebuild test -project apps/macos/Pitboard.xcodeproj -scheme Pitboard \
    -destination 'platform=macOS' -derivedDataPath "$build/UITestData" \
    -clonedSourcePackagesDirPath "$build/SourcePackages" \
    -only-testing PitboardUITests/ScreenshotTests -resultBundlePath "$results"
xcrun xcresulttool export attachments --path "$results" --output-path "$attachments" >/dev/null

# The manifest names each file the test kept by the name the test gave it.
picture() {
    i=0
    while name=$(plutil -extract "0.attachments.$i.suggestedHumanReadableName" raw \
        "$attachments/manifest.json" 2>/dev/null); do
        case $name in "$1"_*)
            file=$(plutil -extract "0.attachments.$i.exportedFileName" raw \
                "$attachments/manifest.json")
            echo "$attachments/$file"
            return
            ;;
        esac
        i=$((i + 1))
    done
    echo "the test kept no picture called $1" >&2
    exit 1
}

menu=$(picture menu)
window=$(picture window)
sips --resampleWidth 600 "$menu" --out docs/images/menu.png >/dev/null
cp docs/images/menu.png .github/media/menu.png
sips --resampleWidth 1520 "$window" --out docs/images/window.png >/dev/null
