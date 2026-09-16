# 第三方组件与开源许可声明 (Third-Party Notices)

本项目（ProcWeaver / 织程代理）自有代码与文档采用 **GNU General Public License v3.0 only (GPL-3.0-only)** 开源，详见根目录 [LICENSE](LICENSE)。

在构建和运行过程中，本项目引用或包含了若干第三方开源库与工具链组件。此文件记录了这些组件的名称、来源、版本、所属版权方及其适用的开源许可证声明。这些第三方许可条件独立于本项目自身的 GPLv3 许可，且不因本项目的许可声明而改变。

---

## 一、第三方组件清单

### 1. C / C++ 原生核心库

| 组件名称 | 来源/仓库 | 许可证类型 | 版权所有者 |
| :--- | :--- | :--- | :--- |
| **MinHook** | [github.com/TsudaKageyu/minhook](https://github.com/TsudaKageyu/minhook) | BSD 2-Clause | Copyright (c) Tsuda Kageyu |
| **Hacker Disassembler Engine (HDE 32/64)** | 嵌入于 MinHook (src/hde) | BSD 2-Clause | Copyright (c) Vyacheslav Patkov |
| **nlohmann/json** | [github.com/nlohmann/json](https://github.com/nlohmann/json) | MIT License | Copyright (c) 2013-2026 Niels Lohmann |

### 2. Rust & Tauri 核心依赖

| 组件名称 | 来源/仓库 | 许可证类型 | 说明 / 版权方 |
| :--- | :--- | :--- | :--- |
| **Tauri (v2.0)** | [tauri.app](https://tauri.app) / [crates.io](https://crates.io/crates/tauri) | Apache-2.0 OR MIT | Tauri 跨平台桌面客户端框架 |
| **tauri-plugin-opener** | [crates.io/crates/tauri-plugin-opener](https://crates.io/crates/tauri-plugin-opener) | Apache-2.0 OR MIT | Tauri 系统默认应用与链接调用插件 |
| **Tokio** | [tokio.rs](https://tokio.rs) | MIT License | 异步事件驱动运行时 |
| **Serde / Serde JSON** | [serde.rs](https://serde.rs) | Apache-2.0 OR MIT | 高性能序列化/反序列化库 |
| **winreg** | [crates.io/crates/winreg](https://crates.io/crates/winreg) | MIT License | Windows 注册表读写库 |
| **dirs-next** | [crates.io/crates/dirs-next](https://crates.io/crates/dirs-next) | Apache-2.0 OR MIT | 标准系统目录探测 |

### 3. 前端与界面依赖 (UI Components)

| 组件名称 | 来源/仓库 | 许可证类型 | 说明 / 版权方 |
| :--- | :--- | :--- | :--- |
| **Vue (v3.5)** | [vuejs.org](https://vuejs.org) | MIT License | Copyright (c) 2018-present Evan You |
| **Lucide Icons** | [lucide.dev](https://lucide.dev) | ISC License | Copyright (c) Lucide Project Authors |
| **Tailwind CSS** | [tailwindcss.com](https://tailwindcss.com) | MIT License | Copyright (c) Tailwind Labs, Inc. |
| **Vite** | [vitejs.dev](https://vitejs.dev) | MIT License | Copyright (c) 2019-present Evan You & Vite Contributors |
| **TypeScript** | [typescriptlang.org](https://www.typescriptlang.org) | Apache-2.0 | Copyright (c) Microsoft Corporation |

### 4. .NET 辅助模块与运行时

| 组件名称 | 说明 | 许可证类型 |
| :--- | :--- | :--- |
| **.NET 10 Desktop Runtime** | Windows 桌面宿主及 DNS 桥接运行依赖 | MIT License (Microsoft .NET) |
| **System.Text.Json** | .NET 原生高性能 JSON 序列化组件 | MIT License |

---

## 二、第三方开源许可证全文

### 1. The MIT License (MIT)

适用于：nlohmann/json, Vue.js, Tailwind CSS, Vite, Tokio, Serde, winreg 等

```text
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

### 2. The BSD 2-Clause License

适用于：MinHook, Hacker Disassembler Engine (HDE)

```text
Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer.

2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

### 3. Apache License 2.0

适用于：Tauri, TypeScript, Rust crates dual-licensed components

```text
Licensed under the Apache License, Version 2.0 (the "License");
you may not use this file except in compliance with the License.
You may obtain a copy of the License at

    http://www.apache.org/licenses/LICENSE-2.0

Unless required by applicable law or agreed to in writing, software
distributed under the License is distributed on an "AS IS" BASIS,
WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
See the License for the specific language governing permissions and
limitations under the License.
```

### 4. ISC License

适用于：Lucide Icons (`lucide-vue-next`)

```text
Permission to use, copy, modify, and/or distribute this software for any
purpose with or without fee is hereby granted, provided that the above
copyright notice and this permission notice appear in all copies.

THE SOFTWARE IS PROVIDED "AS IS" AND THE AUTHOR DISCLAIMS ALL WARRANTIES
WITH REGARD TO THIS SOFTWARE INCLUDING ALL IMPLIED WARRANTIES OF
MERCHANTABILITY AND FITNESS. IN NO EVENT SHALL THE AUTHOR BE LIABLE FOR
ANY SPECIAL, DIRECT, INDIRECT, OR CONSEQUENTIAL DAMAGES OR ANY DAMAGES
WHATSOEVER RESULTING FROM LOSS OF USE, DATA OR PROFITS, WHETHER IN AN
ACTION OF CONTRACT, NEGLIGENCE OR OTHER TORTIOUS ACTION, ARISING OUT OF
OR IN CONNECTION WITH THE USE OR PERFORMANCE OF THIS SOFTWARE.
```
