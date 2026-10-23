# MiHouse

[English](README.md) | [简体中文](README.zh-CN.md)

![MiHouse](docs/images/banner.png)

MiHouse（中文名：米耗子）是一个非官方、本地优先的 Windows 桌面客户端，用于管理小米米家账号，采用 WinUI 3 与 .NET 10 构建。它使用与米家 Android 官方应用相同的账号、云服务和 miot-spec 协议，这些协议来自对官方应用的逆向分析。本项目与小米公司无隶属关系，也未获其认可或支持。

界面由 miot-spec 元数据生成，不复用官方插件，因此设备支持取决于公开规格，而不是米家插件目录。以下列出的是 1.0 已实现的功能，不是路线图。

**从 0.1.x 升级。** 1.0 以包标识运行，数据保存在包数据目录内。未打包的 0.1.x 版本留下的会话与外观设置不会迁移，需要重新登录并重新设置外观。

![MiHouse 首页](docs/images/home.png)

## 功能

- **登录。** 支持二维码或小米账号密码。会话使用当前 Windows 用户的 DPAPI 加密保存，并在下次启动时恢复。需要两步验证的账号会被密码登录拒绝，请改用扫码登录。
- **家庭与房间。** 支持自有与共享家庭、房间归属，以及标题栏中的家庭切换器。
- **设备卡片。** 在房间页和类别页显示在线状态、类别图标、电源控制和传感器读数；首页按设备类别汇总同一批设备。
- **类别图标。** 每个 miot-spec 类别对应一个 Material Design Icons 字形；图标跟随应用主题色，关闭「彩色图标」后转为中性色。
- **外观。** 深浅模式可选跟随系统、浅色或深色；主题色可取自 Windows 强调色或自定义取色；彩色图标与动效可分别关闭。设置保存在 `appearance.json`，修改后即时生效，无需重启。
- **开关支路。** 米家标记为灯的开关支路（云端 `subclass_id`）会归入灯类别；若一个多键开关本体的第一路已有独立行，该本体在房间页隐藏，避免同一路被重复控制。
- **类别三态开关。** 每个设备类别显示开启、关闭或部分开启。应用时只操作可控且在线的设备；离线与只读设备会跳过。
- **场景。** 列出当前家庭的手动场景；停用的场景会标注，每次执行前都会弹出确认。
- **设备详情。** 按 miot-spec 生成常规与高级分区，支持布尔、枚举、范围和文本编辑。修改会立即写入，随后重新读取校验；乐观值在设备确认前标记为未确认，最多等待 12 秒，之后以设备下一次成功读取的值为准。执行动作同样需要确认。
- **被动轮询。** 首页和房间页的卡片每 8 秒刷新；已打开的设备每 6 秒刷新，连续失败后依次退避到 12 秒和 30 秒。窗口失去激活时暂停轮询，已打开的设备不参与卡片轮询。
- **局域网优先。** 拿到设备私网 IP 与 token 时，属性读写和动作走 UDP 上的 miIO；失败后回退云服务，并将该设备的局域网通道封禁 5 分钟。
- **miot-spec 目录。** 先按设备的 `spec_type` 精确查询，再退回型号索引。实例索引 24 小时内视为新鲜，原始文档写入磁盘缓存；网络失败时可使用旧数据。
- **快速启动。** 家庭、设备与场景快照会在云请求返回前先绘制结构。快照 30 天后过期，只恢复结构，不恢复属性值。
- **诊断。** 崩溃报告只包含白名单字段，不记录异常消息。

## 截图

| | |
|:--:|:--:|
| [![首页](docs/images/home.png)](docs/images/home.png) | [![房间页](docs/images/room.png)](docs/images/room.png) |
| **首页** | **房间页** |
| [![设备详情](docs/images/device.png)](docs/images/device.png) | [![场景页](docs/images/scenes.png)](docs/images/scenes.png) |
| **设备详情** | **场景页** |
| [![设置页](docs/images/settings.png)](docs/images/settings.png) | [![窄屏布局](docs/images/narrow.png)](docs/images/narrow.png) |
| **设置页** | **窄屏布局** |

## 环境要求

- Windows 11 24H2（内部版本 26100）或更高版本。
- 构建需要 .NET 10 SDK。
- 以包标识运行及打包需要 `winapp` CLI 0.7.1 或更高版本。
- 安装依赖框架的 MSIX bundle（捆绑包）需要与目标架构（x64 或 ARM64）匹配的 .NET 10 Desktop Runtime 和 Windows App Runtime 2.5.1 或更高版本；证书信任与安装步骤见 `docs/build.md`。

## 构建、运行与测试

```powershell
dotnet build MiHouse.slnx -c Release
dotnet test MiHouse.slnx -c Release
dotnet format MiHouse.slnx --verify-no-changes

winapp run src\MiHouse.App\MiHouse.App.csproj -c Release --detach
winapp package src\MiHouse.App\MiHouse.App.csproj -c Release --arch x64 --arch arm64 --generate-cert
```

`winapp run` 会构建应用、注册开发包标识并启动；`--detach` 让命令立即返回。`winapp package` 会发布 x64 与 ARM64 两个切片，并在当前目录生成已签名的 `.msixbundle` 与自签名证书。构建为依赖框架（framework-dependent）；不使用 `--self-contained`，因为在 winapp 中它只捆绑 Windows App SDK 运行时，不含 .NET。完整的打包与证书流程见 `docs/build.md`。

## 数据与隐私

- **会话。** `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\session.dat`，内容为 JSON，使用 DPAPI（`DataProtectionScope.CurrentUser`）加密。退出登录会删除该文件；文件损坏时按未登录处理。
- **缓存。** miot-spec 文档位于 `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\spec`，以 SHA-256 作为键；快速启动快照位于 `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalCache\snapshots\<userId>`。所有持久化数据都位于包数据目录内，卸载包时随之删除。
- **外观。** `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\appearance.json`，纯 JSON，保存深浅模式、主题色来源与颜色、彩色图标与动效开关。
- **网络。** 登录访问 `account.xiaomi.com` 与 `sts.api.io.mi.com`；签名 API 请求访问 `api.io.mi.com` 或 `<region>.api.io.mi.com`；规格定义来自 `miot-spec.org`。局域网控制直接向设备发送 miIO UDP 包。未实现遥测或分析。
- **权限。** 应用包只声明 `internetClient`（云端 HTTPS）与 `privateNetworkClientServer`（miIO UDP 局域网通信与本地 DNS）两项能力，不持有 `runFullTrust` 之类的广泛系统资源权限。
- **崩溃日志。** `%LOCALAPPDATA%\Packages\<PackageFamilyName>\LocalState\logs\crash-*.log`，最多保留 20 份。字段为白名单：时间戳、应用版本、来源、异常类型、内部异常类型名、反射生成的调用栈，以及固定为 `<not recorded>` 的消息字段。

## 已知限制

- **仅轮询。** 客户端使用旧版米家云会话，不订阅 SSE 或 MQTT 推送，因此在米家 App 中的改动要到下一次轮询才可见。
- **局域网路径未验证。** miIO 传输已实现并有单元测试，但尚未在真实硬件上验证；云控制是已验证路径。
- **官方插件不可复用。** 只存在于米家 React Native 插件中的能力，本客户端没有对应实现。
- **通用回退。** 受支持规格族之外的设备显示通用属性列表；没有 miot-spec 定义的型号会明确提示暂无定义。
- **占位功能。** 收藏与活动尚未实现；设置中的账号管理、家庭成员、通知、耗材和关于带有“未实现”标记。
- **范围。** BLE 设备、Zigbee 子设备、摄像头 P2P 和厂商插件界面不在本版本范围内。

## 架构

```
src/MiHouse.App/       WinUI 3 外壳、视图与视图模型、DPAPI 存储、崩溃日志
src/MiHouse.Core/      认证、签名云客户端、规格目录、miIO 局域网、设备控制
tests/MiHouse.Core.Tests/  Core 层的 xUnit 测试
```

`MiHouse.Core` 不依赖 WinUI。云层负责请求签名与加密；规格层解析并缓存 miot-spec 定义；设备层选择传输方式并处理分体设备的通道寻址。应用层用 `Microsoft.Extensions.DependencyInjection` 组装服务，并把网络工作放在 UI 线程之外。

缓存分三层：会话与解析结果留在内存，原始规格 JSON 写入按哈希命名的磁盘缓存，家庭、设备与场景按用户保存结构快照。快照只读结构，过期值不会被写回设备。

数据流：登录后加载家庭、所选家庭的设备与场景，渲染卡片和规格驱动的详情；编辑立即经局域网或云写入，并通过回读确认。

更多细节见 `docs/architecture.md`、`docs/protocol-notes.md`、`docs/build.md`。

## 许可证

本项目以 **GNU General Public License v3.0 或更高版本**（GPL-3.0-or-later）发布，全文见 [`LICENSE`](LICENSE)。分发修改版本时须同样以 GPL 提供对应源码；本项目不提供任何担保。`MiHouse`、`米耗子` 与小米公司无关，Xiaomi、米家等商标归其各自权利人所有。

设备类别图标来自 Material Design Icons（Apache-2.0，Pictogrammers）。许可全文与字形清单随安装包分发，见 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)。
