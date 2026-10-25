# Google Calendar Desktop

适用于 Windows 11 x64 的轻量 Google 日历托盘工具。点击右下角图标展开日历，点击外部或按 Esc 收起。

## 下载安装

打开 [Releases](https://github.com/Acerocloud/Google-Calendar-Desktop/releases)，选择最新 Windows x64 安装版或便携版。

- 支持 Windows 11 x64；使用系统已有的 .NET Framework 4.8 或更高版本。
- 默认安装到当前用户目录，无需管理员权限。可在 Windows 设置中卸载。
- 启动后图标位于右下角系统托盘，可能在上拉菜单内；不显示任务栏中间的窗口按钮。
- 更新或卸载前，请先右键托盘图标并选择「退出」。卸载保留本机账号数据和设置。
- 安装包未做发布者代码签名。可使用 Release 中的 SHA256SUMS.txt 核对下载文件。

## 连接 Google

点击「Google 登录」，在系统浏览器中选择自己的账号并授权。每位用户读取的是自己有权访问的日历，应用不接收 Google 密码。发行版已包含桌面 OAuth 应用配置，不需要用户导入 JSON。

**授权开放范围由 Google OAuth 项目状态决定。** 如果项目仍为 Testing，维护者需要先把使用者加入测试名单；日历授权与刷新令牌通常在 7 天后到期。发布 GitHub Release 不等于通过 Google OAuth 验证，面向公众使用仍需维护者处理生产发布与所需验证。

## 功能

- 周/月/季/年视图，多个日历与对应颜色，离线缓存。
- 主页和单日日程窗口可新增日程，支持跨天、全天、地点、备注和提醒；登录时一次授予读写权限。日期使用弹出月历，时间使用小时/分钟选择框。
- 新增默认持续 30 分钟，开始时间、结束时间与持续时间自动联动，支持跨天。
- 时间轴默认从 9:00 显示，可上滚到凌晨；Ctrl＋滚轮缩放纵轴并记忆比例。
- 日程详情右上角铅笔可编辑原事件，预填信息，保存后刷新；重复日程只修改本次，冲突时提示刷新。
- 日程详情右上角与当天列表事件右侧提供垃圾箱，弹窗确认删除；重复日程只删除当前实例，只读日历不可删除。
- 深色半透明毛玻璃，统一的事件/当天详情，失焦关闭详情。
- 季度只展示本月日期，弹窗固定在所在屏幕右下角。
- 显示且接通电源每 5 分钟同步，使用电池每 15 分钟，隐藏时每 30 分钟；锁屏和休眠暂停。
- 无广告、无遥测、无额外后台服务。开机启动由用户在设置中选择。

## 自动更新

启动时检查 GitHub 正式 Release，运行中每 6 小时检查一次，也可在设置中点击「检查更新」。发现新版后在应用内提示，点击「一键更新」才会下载安装包、校验 SHA256、安装并重启。已有账号和设置保留。

自动更新支持 1.0.14 及以后的版本，仅识别附有同版本安装包的正式 Release。正在运行目标版本时不会提示同版本更新。仅有便携包的 Release 需手动下载；1.0.9 尚未包含自动更新，需手动安装新版。

右箭头向左翻页、左箭头向右翻页，周/月/季/年与翻页均采用 200ms 整页滑动。空闲预准备最多四页，仅在窗口展开且接通电源时执行，收起释放；无额外网络轮询。

## 从源码构建

需要 Windows 11 x64 和 .NET Framework 4.8。无需 NuGet 或额外浏览器运行时。

1. 在 Google Cloud 创建并配置自己的 Desktop app OAuth 客户端，启用 Calendar API。
2. 将 `source/oauth-client.example.json` 复制为 `source/oauth-client.json`，填写自己的配置；该文件被 Git 忽略，不应提交。
3. 在 PowerShell 中运行 `./Build.ps1`。程序与配置输出到仓库目录。
4. 使用 `./DeskCalendar.exe --self-test --data "一个新的空测试目录"` 运行检查。

公开源码不包含维护者真实 OAuth JSON、账号令牌、日程缓存或构建工具。发布的程序已预置发行用桌面客户端配置；桌面客户端配置不能作为保密服务端凭据使用。

## 手动打包安装版

安装 [Inno Setup](https://jrsoftware.org/isdl.php)。把程序、配置和随附说明放入 `dist/app`，明确需要打包时再运行：

```powershell
./installer/Build-Installer.ps1 -CompilerPath "C:/path/to/ISCC.exe" -PayloadDir "./dist/app"
```

安装脚本支持当前用户安装、开始菜单入口、可选桌面快捷方式、版本升级和卸载。不会打包用户数据目录。不要把 Google 令牌、缓存或个人配置放入 payload。

不会自动打包或自动发布。只有用户明确提出对应要求时，才制作安装版或发布到 GitHub，见 [AGENTS.md](AGENTS.md)。

## 验证和隐私

1.0.9 的 88 项检查通过；首个安装版通过隔离安装、安装后自检、文件哈希比对与卸载测试。

- [验证记录](docs/verification.md)
- [隐私说明](PRIVACY.md)
- [Google 授权与测试状态](https://developers.google.com/identity/protocols/oauth2)

## 许可

应用源码使用 MIT 许可，见 [LICENSE](LICENSE)。Google 名称与彩色 G 图标属于 Google；图标取自 [Google 官方登录品牌资源](https://developers.google.com/identity/branding-guidelines)，使用须遵守相应品牌规则。本项目不是 Google 官方产品。