fn main() {
    println!("cargo:rerun-if-changed=resources/origin.rc");
    let windows = tauri_build::WindowsAttributes::new()
        .append_rc_content(include_str!("resources/origin.rc"));
    tauri_build::try_build(tauri_build::Attributes::new().windows_attributes(windows))
        .expect("Tauri 资源构建失败");
}
