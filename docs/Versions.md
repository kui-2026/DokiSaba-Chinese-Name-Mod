# 版本、依赖与实机记录

| 已验证 DLL | 插件版本 | 实机覆盖 |
| --- | --- | --- |
| Dokisaba.P0.dll | 0.4.0 | 简中汉字姓名输入，以及界面与剧情字幕显示 |
| Dokisaba.P1.dll | 0.1.4 | 日语 IME、粘贴、候选搜索和选择 |
| Dokisaba.P2.dll | 0.2.1 | Tab 试听，以及手塚国光等已观察到的剧情称呼 |

本次保存的是实机版本原件。不同角色的录音覆盖不同。可用录音仍需由原版剧情触发，试听和剧情播放应分别验收。

- 游戏 Steam Build 26584781，Unity 6000.0.61f1，Windows x64 IL2CPP。GameAssembly.dll SHA256 为 `FFA76F32E04638CF4A117104B105FFC9F8B560F5C1A1269E739689A2D2FB133D`。更新游戏后应重新核对方法和原生补丁位置。
- BepInEx 6.0.0-be.793+dbc82f0 Windows IL2CPP x64。编译引用自己的 BepInEx/core 目录。P0/P1 使用其 HarmonyX（0Harmony 2.10.2）和 Il2CppInterop.Runtime。P2 使用 BepInEx.Core 与 BepInEx.Unity.IL2CPP。
- P0/P1 引用本机加载器实际生成的 BepInEx/interop/App.Game.dll，P1 另需 Message.dll。使用与目标安装匹配的 Interop。
- 目标框架 net6.0，验证 SDK 6.0.428。三个项目的 NuGet.Config 清空外部源，引用来自本机加载器。
- 发布文件哈希见 release/SHA256SUMS.txt。编译输出布局、依赖路径和发布目录是这次备份中整理的内容，发布 DLL 未重编译。

卸载或覆盖加载器前请先记录安装时添加的文件。Mod 备份范围为项目和可部署插件，存档另行保管。
