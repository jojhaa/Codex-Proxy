use serde::Deserialize;
use std::{path::Path, process::Stdio, time::Duration};
use tokio::io::{AsyncBufReadExt, BufReader};

#[derive(Deserialize)]
pub struct ClientResult {
    #[serde(default)] pub path: String,
    #[serde(default)] pub pid: u32,
    pub error: Option<String>,
}

pub async fn request(base: &Path, operation: &str, target_path: Option<&str>) -> Result<ClientResult, String> {
    let mut cmd = tokio::process::Command::new(base.join("codex_client_host.exe"));
    cmd.arg(operation);
    if let Some(p) = target_path {
        let trimmed = p.trim();
        if !trimmed.is_empty() {
            cmd.arg(trimmed);
        }
    }
    let mut child = cmd.current_dir(base).creation_flags(0x08000000)
        .stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::null())
        .kill_on_drop(true).spawn().map_err(|e| format!("无法启动客户端定位/启动组件：{e}"))?;
    let stdout = child.stdout.take().ok_or("无法读取客户端定位结果")?;
    let result = tokio::time::timeout(Duration::from_secs(35), async {
        // Read one protocol line, not EOF: launched clients may inherit a
        // console handle and outlive this short-lived helper.
        let mut lines = BufReader::new(stdout).lines();
        let json = loop {
            let line = lines.next_line().await.map_err(|e| e.to_string())?
                .ok_or("客户端组件未返回结果")?;
            if let Some(json) = line.strip_prefix("CODEX_CLIENT_RESULT=") { break json.to_owned(); }
        };
        let status = child.wait().await.map_err(|e| e.to_string())?;
        let response: ClientResult = serde_json::from_str(&json)
            .map_err(|_| "客户端定位/启动组件返回无效结果".to_string())?;
        if let Some(error) = response.error.as_ref() { return Err(error.clone()); }
        if !status.success() || response.path.is_empty() || (operation != "resolve" && response.pid == 0) {
            return Err("未确认客户端启动成功".to_string());
        }
        Ok(response)
    }).await;
    result.map_err(|_| "自动定位或启动超时，请检查客户端安装与运行状态".to_string())?
}

#[cfg(test)]
mod tests {
    use super::*;
    #[tokio::test]
    #[ignore = "requires a built helper and the installed desktop client"]
    async fn installed_helper_protocol() {
        let directory = std::env::var("CODEX_CLIENT_HOST_TEST_DIR").expect("test helper directory");
        let result = request(Path::new(&directory), "resolve", None).await.unwrap();
        assert!(Path::new(&result.path).is_file());
        assert_eq!(result.pid, 0);
        assert!(request(Path::new(&directory), "invalid-operation", None).await.is_err());
    }
}
