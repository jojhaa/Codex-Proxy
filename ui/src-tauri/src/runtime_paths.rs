use std::path::{Path, PathBuf};

// Resolve the runtime directory once, independently of whether a particular
// mode's configuration or launcher.settings.json has already been created.
pub fn runtime_dir(executable: &Path) -> PathBuf {
    let directory = executable.parent().unwrap_or_else(|| Path::new("."));
    for ancestor in directory.ancestors().take(6) {
        if ancestor.join("config.json").is_file() {
            return ancestor.to_path_buf();
        }
        let packaged = ancestor.join("dist");
        if packaged.join("config.json").is_file() {
            return packaged;
        }
    }
    directory.to_path_buf()
}

pub fn config_path(base: &Path, mode: &str) -> PathBuf {
    if mode == "process" {
        base.join("process").join("config.json")
    } else {
        base.join("config.json")
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::fs;
    use std::sync::atomic::{AtomicUsize, Ordering};
    static SEQUENCE: AtomicUsize = AtomicUsize::new(0);

    struct Fixture(PathBuf);
    impl Fixture {
        fn new() -> Self {
            let path = std::env::temp_dir().join(format!("codex-path-test-{}-{}",
                std::process::id(), SEQUENCE.fetch_add(1, Ordering::Relaxed)));
            fs::create_dir_all(&path).unwrap();
            Self(path)
        }
        fn config(&self) -> PathBuf {
            let dist = self.0.join("dist");
            fs::create_dir_all(&dist).unwrap();
            fs::write(dist.join("config.json"), "{}").unwrap();
            dist
        }
    }
    impl Drop for Fixture {
        fn drop(&mut self) { let _ = fs::remove_dir_all(&self.0); }
    }

    #[test]
    fn first_mode_save_in_packaged_directory() {
        let fixture = Fixture::new();
        let dist = fixture.config();
        let base = runtime_dir(&dist.join("ProcWeaver.exe"));
        assert_eq!(base, dist);
        let path = base.join("launcher.settings.json");
        for mode in ["process", "standard", "process"] {
            fs::write(&path, mode).unwrap();
            assert_eq!(fs::read_to_string(&path).unwrap(), mode);
        }
        assert!(!dist.join("dist").exists());
    }

    #[test]
    fn development_executable_uses_project_dist() {
        let fixture = Fixture::new();
        let dist = fixture.config();
        let exe = fixture.0.join("ui/src-tauri/target/debug/codex_proxy_ui.exe");
        assert_eq!(runtime_dir(&exe), dist);
    }

    #[test]
    fn missing_process_config_stays_in_same_runtime() {
        let fixture = Fixture::new();
        let dist = fixture.config();
        assert_eq!(config_path(&dist, "process"), dist.join("process/config.json"));
        assert_eq!(config_path(&dist, "standard"), dist.join("config.json"));
    }

    #[test]
    fn portable_without_config_uses_executable_directory() {
        let fixture = Fixture::new();
        assert_eq!(runtime_dir(&fixture.0.join("app.exe")), fixture.0);
    }
}
