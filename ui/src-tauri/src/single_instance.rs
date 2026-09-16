use std::io;
use std::sync::{atomic::{AtomicBool, Ordering}, Arc, Mutex};
use std::thread::JoinHandle;

#[link(name = "kernel32")]
unsafe extern "system" {
    fn CreateMutexW(attributes: *const (), owner: i32, name: *const u16) -> isize;
    fn CreateEventW(attributes: *const (), manual: i32, initial: i32, name: *const u16) -> isize;
    fn GetLastError() -> u32;
    fn SetEvent(handle: isize) -> i32;
    fn WaitForSingleObject(handle: isize, milliseconds: u32) -> u32;
    fn CloseHandle(handle: isize) -> i32;
}

#[link(name = "user32")]
unsafe extern "system" {
    fn ShowWindow(window: isize, command: i32) -> i32;
}

pub fn restore_window(window: isize) {
    // Also restore windows hidden/minimized outside the WebView framework.
    unsafe { ShowWindow(window, 9); } // SW_RESTORE
}

pub struct SingleInstance {
    mutex: isize,
    event: isize,
    stop: Arc<AtomicBool>,
    worker: Mutex<Option<JoinHandle<()>>>,
}

impl SingleInstance {
    pub fn acquire(name: &str) -> io::Result<Option<Self>> {
        let wide = |s: String| s.encode_utf16().chain(Some(0)).collect::<Vec<_>>();
        // Create the notification event first, so a concurrent second launch
        // can notify even before the first window is ready.
        let event_name = wide(format!("{name}.activate"));
        let mutex_name = wide(format!("{name}.instance"));
        unsafe {
            let event = CreateEventW(std::ptr::null(), 0, 0, event_name.as_ptr());
            if event == 0 { return Err(io::Error::last_os_error()); }
            let mutex = CreateMutexW(std::ptr::null(), 0, mutex_name.as_ptr());
            let error = GetLastError();
            if mutex == 0 {
                CloseHandle(event);
                return Err(io::Error::from_raw_os_error(error as i32));
            }
            if error == 183 { // ERROR_ALREADY_EXISTS
                let notified = SetEvent(event);
                let error = GetLastError();
                CloseHandle(mutex);
                CloseHandle(event);
                if notified == 0 { return Err(io::Error::from_raw_os_error(error as i32)); }
                return Ok(None);
            }
            Ok(Some(Self { mutex, event, stop: Arc::new(AtomicBool::new(false)), worker: Mutex::new(None) }))
        }
    }

    pub fn listen(&self, activate: impl Fn() + Send + 'static) {
        let mut worker = self.worker.lock().unwrap();
        if worker.is_some() { return; }
        let event = self.event;
        let stop = self.stop.clone();
        *worker = Some(std::thread::spawn(move || {
            while !stop.load(Ordering::Acquire) {
                let result = unsafe { WaitForSingleObject(event, u32::MAX) };
                if result != 0 || stop.load(Ordering::Acquire) { break; }
                activate();
            }
        }));
    }
}

impl Drop for SingleInstance {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Release);
        unsafe { SetEvent(self.event); }
        if let Some(worker) = self.worker.get_mut().unwrap().take() { let _ = worker.join(); }
        unsafe { CloseHandle(self.event); CloseHandle(self.mutex); }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn duplicate_notifies_before_window_ready_and_exit_releases_lock() {
        let name = format!("Local\\CodexProxy.Test.{}", std::process::id());
        let first = SingleInstance::acquire(&name).unwrap().unwrap();
        assert!(SingleInstance::acquire(&name).unwrap().is_none());
        let (send, receive) = std::sync::mpsc::channel();
        first.listen(move || { let _ = send.send(()); });
        receive.recv_timeout(std::time::Duration::from_secs(2)).unwrap();
        assert!(SingleInstance::acquire(&name).unwrap().is_none());
        receive.recv_timeout(std::time::Duration::from_secs(2)).unwrap();
        drop(first);
        assert!(SingleInstance::acquire(&name).unwrap().is_some());
    }
    #[test]
    fn concurrent_launches_have_one_owner() {
        let name = format!("Local\\CodexProxy.Concurrent.{}", std::process::id());
        let barrier = Arc::new(std::sync::Barrier::new(8));
        let workers: Vec<_> = (0..8).map(|_| {
            let name = name.clone(); let barrier = barrier.clone();
            std::thread::spawn(move || {
                barrier.wait();
                let owner = SingleInstance::acquire(&name).unwrap();
                barrier.wait();
                owner.is_some()
            })
        }).collect();
        assert_eq!(workers.into_iter().map(|w| w.join().unwrap() as usize).sum::<usize>(), 1);
    }
}
