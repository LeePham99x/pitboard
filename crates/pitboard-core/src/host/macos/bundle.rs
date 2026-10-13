use core_foundation::bundle::CFBundle;
use core_foundation::string::CFString;
use core_foundation::url::CFURL;
use std::path::Path;

pub(super) fn identifier(bundle: &Path) -> Option<String> {
    said(bundle, "CFBundleIdentifier")
}

pub(super) fn version(bundle: &Path) -> Option<String> {
    said(bundle, "CFBundleShortVersionString")
}

fn said(bundle: &Path, key: &'static str) -> Option<String> {
    let url = CFURL::from_path(bundle, true)?;
    let info = CFBundle::new(url)?.info_dictionary();
    info.find(CFString::from_static_string(key))?
        .downcast::<CFString>()
        .map(|value| value.to_string())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_bundle_says_its_identifier_and_version_and_a_folder_that_is_none_says_nothing() {
        let root = std::env::temp_dir().join(format!("pitboard-bundle-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&root);
        let app = root.join("Scratch.app");
        std::fs::create_dir_all(app.join("Contents")).expect("a scratch bundle");
        std::fs::write(
            app.join("Contents/Info.plist"),
            r#"<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.example.scratch</string>
<key>CFBundleShortVersionString</key><string>2.31226.1</string>
</dict></plist>"#,
        )
        .expect("a plist");
        assert_eq!(identifier(&app).as_deref(), Some("com.example.scratch"));
        assert_eq!(version(&app).as_deref(), Some("2.31226.1"));
        assert_eq!(identifier(&root.join("Missing.app")), None);
        assert_eq!(version(&root.join("Missing.app")), None);
        let _ = std::fs::remove_dir_all(&root);
    }
}
