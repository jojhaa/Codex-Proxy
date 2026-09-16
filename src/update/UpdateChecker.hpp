#pragma once

namespace UpdateChecker {
    // 后台检查 GitHub Release。函数内部会检查 updates.enabled，默认不联网。
    void StartAsync();
}
