use std::sync::atomic::{AtomicBool, Ordering};
pub static FULL_LOGS: AtomicBool = AtomicBool::new(false);
#[derive(Default)]
pub struct LogBatch { lines: Vec<String>, bytes: usize, loopback: u64, omitted: u64 }
impl LogBatch {
    pub fn flush_full(&self) -> bool {
        FULL_LOGS.load(Ordering::Relaxed) && (self.lines.len() >= 64 || self.bytes >= 128 * 1024)
    }
    pub fn add(&mut self, text: &str) {
        let full = FULL_LOGS.load(Ordering::Relaxed);
        let error = ["错误", "失败", "警告", "error", "ERROR", "WARN"].iter().any(|word| text.contains(word));
        if !full && !error && !text.contains("[日志汇总]") && ["loopback", "本地回环", "本地直连"].iter().any(|word| text.contains(word)) {
            self.loopback = self.loopback.saturating_add(1); return;
        }
        let limit = if full { 65507 } else { 2000 };
        let mut end = text.len().min(limit);
        while !text.is_char_boundary(end) { end -= 1; }
        if error {
            while !self.lines.is_empty() && (self.lines.len() >= 128 || self.bytes + end + 20 > 256 * 1024) {
                self.bytes -= self.lines.remove(0).len(); self.omitted = self.omitted.saturating_add(1);
            }
        }
        if self.lines.len() >= 128 || self.bytes + end + 20 > 256 * 1024 {
            self.omitted = self.omitted.saturating_add(1); return;
        }
        let mut line = text[..end].to_owned();
        if end != text.len() { line.push_str("…[已截断]"); }
        self.bytes += line.len(); self.lines.push(line);
    }
    pub fn take(&mut self, summary: bool) -> Vec<String> {
        let mut lines = std::mem::take(&mut self.lines); self.bytes = 0;
        if summary && self.loopback > 0 {
            lines.push(format!("[日志汇总] 后端合并 {} 条本地回环日志", self.loopback)); self.loopback = 0;
        }
        if self.omitted > 0 {
            lines.push(format!("[警告] 接收队列已满，省略 {} 条日志", self.omitted)); self.omitted = 0;
        }
        lines
    }
}
#[cfg(test)]
mod tests {
    use super::*;
    #[test] fn flood_is_bounded_and_errors_remain() {
        let mut batch = LogBatch::default();
        for _ in 0..100000 { batch.add("本地回环成功"); }
        assert!(batch.lines.is_empty());
        batch.add("本地回环连接失败");
        let lines = batch.take(true);
        assert_eq!(lines.len(), 2); assert!(lines[0].contains("失败"));
        for _ in 0..10000 { batch.add(&"普通消息".repeat(1000)); }
        assert!(batch.lines.len() <= 128 && batch.bytes <= 256 * 1024);
    }
}
