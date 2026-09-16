#[link(name = "user32")]
unsafe extern "system" {
    fn GetAsyncKeyState(key: i32) -> i16;
    fn GetSystemMetrics(index: i32) -> i32;
}

// Native window movement can consume mouseup before it reaches the WebView.
#[tauri::command]
pub fn is_drag_button_down() -> bool {
    unsafe {
        let primary = if GetSystemMetrics(23) != 0 { 2 } else { 1 }; // SM_SWAPBUTTON
        GetAsyncKeyState(primary) < 0
    }
}
