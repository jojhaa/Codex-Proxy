// Copyright (c) Time Silent. SPDX-License-Identifier: GPL-3.0-only
import metadata from '../../project-info.json';
import tauri from '../src-tauri/tauri.conf.json';

export const projectInfo = Object.freeze({ ...metadata, version: tauri.version });
export const diagnosticHeader = () =>
  `${projectInfo.name} v${projectInfo.version}\n作者：${projectInfo.author}\n项目：${projectInfo.repository}\n许可证：${projectInfo.license}\n`;
