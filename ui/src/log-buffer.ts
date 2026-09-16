export interface LogEntry { id: number; raw: string }
export class LogBuffer {
  full = false;
  private entries: LogEntry[] = [];
  private bytes = 0;
  private nextId = 1;
  private loopback = 0;
  private changed = false;
  private lastSummary = 0;
  private errors = new Map<string, number>();
  private time = new Intl.DateTimeFormat('zh-CN', { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
  get size() { return this.entries.length; }
  get textBytes() { return this.bytes; }
  add(message: string, now = Date.now()) {
    if (!this.full) {
      message = message.length > 1024 ? message.slice(0, 1000) + '…[已截断]' : message;
      const error = /错误|失败|警告|error|fail|warn/i.test(message);
      if (!error && /loopback|本地回环|本地直连/i.test(message) && !message.includes('[日志汇总]')) {
        this.loopback++; return;
      }
      if (error) {
        const key = message.replace(/^\[[\d:. ]+\]\s*/, '');
        if (this.errors.has(key)) { this.errors.set(key, this.errors.get(key)! + 1); return; }
        if (this.errors.size >= 64) this.errors.delete(this.errors.keys().next().value!);
        this.errors.set(key, 0);
      }
    }
    this.push(`[${this.time.format(now)}] ${message}`);
  }
  private push(raw: string) {
    this.entries.push({ id: this.nextId++, raw }); this.bytes += raw.length * 2;
    this.trim(); this.changed = true;
  }
  private trim() {
    if (this.full) return;
    if (this.entries.length <= 200 && this.bytes <= 256 * 1024) return;
    let start = this.entries.length, bytes = 0;
    while (start > 0 && this.entries.length - start < 200) {
      const size = this.entries[start - 1]!.raw.length * 2;
      if (bytes + size > 256 * 1024) break;
      bytes += size; start--;
    }
    this.entries = this.entries.slice(start); this.bytes = bytes;
  }
  flush(now = Date.now()): LogEntry[] | null {
    if (now - this.lastSummary >= 1000) {
      if (this.loopback) { this.push(`[日志汇总] 合并 ${this.loopback} 条本地回环日志`); this.loopback = 0; }
      let repeated = 0;
      for (const value of this.errors.values()) repeated += value;
      if (repeated) this.push(`[警告] 合并 ${repeated} 条重复警报；首次详情保留在最近日志中`);
      this.errors.clear(); this.lastSummary = now;
    }
    if (!this.changed) return null;
    this.changed = false;
    return this.entries.slice(-500);
  }
  setFull(enabled: boolean) {
    this.flush(); this.full = enabled; this.errors.clear(); this.trim(); this.changed = true;
  }
  clear() {
    this.entries = []; this.bytes = 0; this.loopback = 0; this.errors.clear(); this.changed = true;
  }
  copyText() { return this.entries.map(entry => entry.raw).join('\n'); }
}
