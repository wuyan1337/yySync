# Music Steam RPC

### ✨ 主要功能

- 🎵 **多平台支持**: 网易云音乐、QQ 音乐、洛雪音乐 PC 客户端
- 📡 **Steam 同步**: 通过 SteamKit2 连接 Steam 网络，将播放状态显示为非 Steam 游戏
- 📻 **模式兼容**: 支持网易云音乐 FM 电台和漫游模式
- 🎨 **高度自定义**:
  - 图形化设置界面，支持实时预览
  - 可选显示歌手名、进度条
  - 支持开机自启、最小化到托盘

---

### 📥 安装与使用

1. 确保系统已安装 **[.NET 9](https://dotnet.microsoft.com/download/dotnet/9.0)** 运行库  
2. 下载最新版 `yySync.exe` 运行  
3. 首次运行时需登录 Steam 帐号（支持 Steam Guard / 手机验证）  
4. 登录成功后，打开音乐播放器即可自动同步状态到 Steam
5. 洛雪音乐用户请注意 请在洛雪音乐 - 设置 - 开放API - 启用开放API服务，允许来自局域网的访问

---

### 🙏 致谢

本项目在开发过程中参考了以下优秀开源项目的设计思路与实现方式：

- [ArchiSteamFarm](https://github.com/JustArchiNET/ArchiSteamFarm)
- [Music-DiscordRPC](https://github.com/kriYamiHikari/Music-DiscordRPC)
- [NetEase-Cloud-Music-DiscordRPC](https://github.com/Kxnrl/NetEase-Cloud-Music-DiscordRPC)

感谢原作者们对开源社区的贡献 ❤️
