using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;
namespace MusicRpc;
internal class SteamSessionManager : IDisposable
{
    private SteamClient? _steamClient;
    private CallbackManager? _callbackManager;
    private SteamUser? _steamUser;
    private SteamFriends? _steamFriends;
    private readonly CancellationTokenSource _cts = new();
    private Task? _callbackTask;
    private bool _isRunning;
    private string _currentGameName = string.Empty;
    private readonly ManualResetEventSlim _connectedEvent = new(false);
    public bool IsConnected => _steamClient?.IsConnected ?? false;
    public bool IsLoggedOn { get; private set; }
    public string? Username { get; private set; }
    public string? LoginError { get; private set; }
    public event Action<bool>? OnSteamGuardRequired; 
    private TaskCompletionSource<string>? _guardCodeTcs;
    public void Start()
    {
        if (_isRunning) return;
        _isRunning = true;
        _steamClient = new SteamClient();
        _callbackManager = new CallbackManager(_steamClient);
        _steamUser = _steamClient.GetHandler<SteamUser>()!;
        _steamFriends = _steamClient.GetHandler<SteamFriends>()!;
        _callbackManager.Subscribe<SteamClient.ConnectedCallback>(_ =>
        {
            Debug.WriteLine("[SteamSession] 已连接到 Steam");
            _connectedEvent.Set();
        });
        _callbackManager.Subscribe<SteamClient.DisconnectedCallback>(cb =>
        {
            IsLoggedOn = false;
            _connectedEvent.Reset();
            Debug.WriteLine($"[SteamSession] 已断开连接 (UserInitiated={cb.UserInitiated})");
            if (!cb.UserInitiated && _isRunning)
            {
                Task.Run(async () =>
                {
                    await Task.Delay(5000);
                    if (_isRunning)
                    {
                        Debug.WriteLine("[SteamSession] 尝试自动重连...");
                        _steamClient?.Connect();
                    }
                });
            }
        });
        _callbackManager.Subscribe<SteamUser.LoggedOnCallback>(cb =>
        {
            if (cb.Result == EResult.OK)
            {
                IsLoggedOn = true;
                LoginError = null;
                Debug.WriteLine($"[SteamSession] 登录成功! SteamID: {cb.ClientSteamID}");
                _steamFriends?.SetPersonaState(EPersonaState.Online);
                Debug.WriteLine("[SteamSession] 已设置在线状态");
            }
            else
            {
                IsLoggedOn = false;
                LoginError = cb.Result.ToString();
                Debug.WriteLine($"[SteamSession] 登录失败: {cb.Result} / {cb.ExtendedResult}");
            }
        });
        _callbackManager.Subscribe<SteamUser.LoggedOffCallback>(cb =>
        {
            IsLoggedOn = false;
            Debug.WriteLine($"[SteamSession] 已登出: {cb.Result}");
        });
        _callbackTask = Task.Run(() => CallbackLoop(_cts.Token));
        _steamClient.Connect();
        Debug.WriteLine("[SteamSession] 正在连接到 Steam...");
    }
    public bool WaitForConnection(int timeoutMs = 8000)
    {
        return _connectedEvent.Wait(timeoutMs);
    }
    public async Task<bool> LoginAsync(string username, string password)
    {
        if (_steamClient == null)
        {
            LoginError = "Steam 客户端未初始化";
            return false;
        }
        if (!IsConnected)
        {
            if (!WaitForConnection())
            {
                LoginError = "无法连接到 Steam 服务器";
                return false;
            }
        }
        Username = username;
        LoginError = null;
        try
        {
            var authSession = await _steamClient.Authentication.BeginAuthSessionViaCredentialsAsync(
                new AuthSessionDetails
                {
                    Username = username,
                    Password = password,
                    IsPersistentSession = true,
                    Authenticator = new SteamGuardAuthenticator(this),
                    GuardData = Configurations.Instance.Settings.SteamGuardData
                }
            ).ConfigureAwait(false);
            var pollResult = await authSession.PollingWaitForResultAsync().ConfigureAwait(false);
            if (!string.IsNullOrEmpty(pollResult.NewGuardData))
            {
                Configurations.Instance.Settings.SteamGuardData = pollResult.NewGuardData;
                Configurations.Instance.Save();
            }
            _steamUser!.LogOn(new SteamUser.LogOnDetails
            {
                Username = username,
                AccessToken = pollResult.RefreshToken,
                LoginID = 1243,
                ShouldRememberPassword = true
            });
            for (var i = 0; i < 30; i++)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (IsLoggedOn)
                {
                    Configurations.Instance.Settings.SteamUsername = username;
                    Configurations.Instance.Settings.SteamRefreshToken = pollResult.RefreshToken;
                    Configurations.Instance.Save();
                    return true;
                }
                if (LoginError != null)
                {
                    return false;
                }
            }
            LoginError = "登录超时";
            return false;
        }
        catch (AuthenticationException ex)
        {
            LoginError = $"认证失败: {ex.Result} - {ex.Message}";
            Debug.WriteLine($"[SteamSession] {LoginError}");
            return false;
        }
        catch (Exception ex)
        {
            LoginError = $"登录异常: {ex.Message}";
            Debug.WriteLine($"[SteamSession] {LoginError}");
            return false;
        }
    }
    public async Task<bool> LoginWithTokenAsync(string username, string refreshToken)
    {
        if (_steamClient == null)
        {
            LoginError = "Steam 客户端未初始化";
            return false;
        }
        if (!IsConnected)
        {
            if (!WaitForConnection())
            {
                LoginError = "无法连接到 Steam 服务器";
                return false;
            }
        }
        Username = username;
        LoginError = null;
        _steamUser!.LogOn(new SteamUser.LogOnDetails
        {
            Username = username,
            AccessToken = refreshToken,
            LoginID = 1243,
            ShouldRememberPassword = true
        });
        for (var i = 0; i < 20; i++)
        {
            await Task.Delay(500).ConfigureAwait(false);
            if (IsLoggedOn) return true;
            if (LoginError != null) break;
        }
        Debug.WriteLine("[SteamSession] Token 登录失败");
        Configurations.Instance.Settings.SteamRefreshToken = "";
        Configurations.Instance.Save();
        return false;
    }
    public Task SetGameNameAsync(string gameName)
    {
        if (!IsLoggedOn || _steamClient == null) return Task.CompletedTask;
        if (gameName == _currentGameName) return Task.CompletedTask;
        Debug.WriteLine($"[SteamSession] 正在设置游戏名称: '{gameName}'");

        if (!string.IsNullOrEmpty(gameName))
        {
            var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob)
            {
                Body =
                {
                    client_os_type = unchecked((uint)EOSType.Windows10)
                }
            };
            request.Body.games_played.Add(new CMsgClientGamesPlayed.GamePlayed
            {
                game_extra_info = gameName,
                game_id = new GameID
                {
                    AppType = GameID.GameType.Shortcut,
                    ModID = uint.MaxValue
                }
            });
            _steamClient.Send(request);
            Debug.WriteLine($"[SteamSession] CMsgClientGamesPlayed 已发送: '{gameName}'");
        }
        _currentGameName = gameName;
        return Task.CompletedTask;
    }
    public void ClearGameName()
    {
        if (!IsLoggedOn || _steamClient == null) return;
        var request = new ClientMsgProtobuf<CMsgClientGamesPlayed>(EMsg.ClientGamesPlayedWithDataBlob)
        {
            Body =
            {
                client_os_type = unchecked((uint)EOSType.Windows10)
            }
        };
        _steamClient.Send(request);
        _currentGameName = string.Empty;
        Debug.WriteLine("[SteamSession] 游戏名称已清除");
    }
    public void SubmitSteamGuardCode(string code)
    {
        _guardCodeTcs?.TrySetResult(code);
    }
    private void CallbackLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _isRunning)
        {
            _callbackManager?.RunWaitCallbacks(TimeSpan.FromSeconds(1));
        }
    }
    private class SteamGuardAuthenticator(SteamSessionManager manager) : IAuthenticator
    {
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            manager._guardCodeTcs = new TaskCompletionSource<string>();
            manager.OnSteamGuardRequired?.Invoke(true); 
            return manager._guardCodeTcs.Task;
        }
        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            manager._guardCodeTcs = new TaskCompletionSource<string>();
            manager.OnSteamGuardRequired?.Invoke(false); 
            return manager._guardCodeTcs.Task;
        }
        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            manager.OnSteamGuardRequired?.Invoke(true);
            return Task.FromResult(true);
        }
    }
    public void Dispose()
    {
        _isRunning = false;
        _cts.Cancel();
        if (IsLoggedOn)
        {
            ClearGameName();
            _steamUser?.LogOff();
        }
        _steamClient?.Disconnect();
        _callbackTask?.Wait(3000);
        _cts.Dispose();
        _connectedEvent.Dispose();
    }
}