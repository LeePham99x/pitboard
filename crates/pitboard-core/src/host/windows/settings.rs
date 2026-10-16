use crate::host::WindowsSettings;
use windows_registry::{CURRENT_USER, LOCAL_MACHINE};

pub(super) fn read() -> WindowsSettings {
    WindowsSettings {
        wsl: wsl(),
        proxy: proxy(),
        admin_approval: admin_approval(),
    }
}

// Where Microsoft's "How to manage WSL disk space" finds a distribution, by its name.
fn wsl() -> Vec<String> {
    let Ok(lxss) = CURRENT_USER.open(r"Software\Microsoft\Windows\CurrentVersion\Lxss") else {
        return Vec::new();
    };
    let Ok(keys) = lxss.keys() else {
        return Vec::new();
    };
    keys.filter_map(|key| lxss.open(key).ok()?.get_string("DistributionName").ok())
        .filter(|name| !name.trim().is_empty())
        .collect()
}

// As hyper-util 0.1.20's `win::with_system` reads them.
fn proxy() -> Option<String> {
    let settings = CURRENT_USER
        .open(r"Software\Microsoft\Windows\CurrentVersion\Internet Settings")
        .ok()?;
    if settings.get_u32("ProxyEnable").unwrap_or(0) == 0 {
        return None;
    }
    settings
        .get_string("ProxyServer")
        .ok()
        .filter(|server| !server.trim().is_empty())
}

fn admin_approval() -> Option<bool> {
    LOCAL_MACHINE
        .open(r"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System")
        .ok()?
        .get_u32("EnableLUA")
        .ok()
        .map(|lua| lua != 0)
}

#[cfg(test)]
mod tests {
    use super::*;

    // Read only. RF-A1 read `EnableLUA` 1 on both images, and no runner has WSL.
    #[test]
    fn this_accounts_settings_are_read_as_codex_and_windows_read_them() {
        let read = read();
        assert!(
            read.admin_approval.is_some(),
            "every Windows keeps EnableLUA"
        );
        assert!(read.wsl.iter().all(|name| !name.trim().is_empty()));
        assert!(
            read.proxy
                .as_deref()
                .is_none_or(|server| !server.trim().is_empty())
        );
    }
}
