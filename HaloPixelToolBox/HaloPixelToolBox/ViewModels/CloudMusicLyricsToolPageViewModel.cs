using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Core.Utilities;
using HaloPixelToolBox.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Utilities;
using System.Diagnostics;
using XFEExtension.NetCore.StringExtension;
using XFEExtension.NetCore.WinUIHelper.Implements;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.ViewModels;

public partial class CloudMusicLyricsToolPageViewModel : ServiceBaseViewModelBase<string>
{
    [ObservableProperty]
    private bool deviceReady;
    [ObservableProperty]
    private bool cloudMusicReady;
    [ObservableProperty]
    private bool enableCloudMusicLyrics = CloudMusicLyricsProfile.EnableCloudMusicLyrics;
    [ObservableProperty]
    private bool switchBackWhenPause = CloudMusicLyricsProfile.SwitchBackWhenPause;
    [ObservableProperty]
    private bool useInputedAddress = CloudMusicLyricsProfile.UseInputedAddress;
    [ObservableProperty]
    private string inputedAddress = CloudMusicLyricsProfile.InputedAddress;
    [ObservableProperty]
    private int switchBackTimeout = CloudMusicLyricsProfile.SwitchBackTimeout;
    [ObservableProperty]
    private bool enableScreenColorSync = CloudMusicLyricsProfile.EnableScreenColorSync;
    [ObservableProperty]
    private bool enableAmbientColorSync = CloudMusicLyricsProfile.EnableAmbientColorSync;
    [ObservableProperty]
    private Core.Models.Lighting.AmbientLightEffect syncAmbientLightEffect = CloudMusicLyricsProfile.SyncAmbientLightEffect;
    [ObservableProperty]
    private Core.Models.Lighting.AmbientLightBrightness syncAmbientLightBrightness = CloudMusicLyricsProfile.SyncAmbientLightBrightness;
    [ObservableProperty]
    private int syncAmbientLightSpeed = CloudMusicLyricsProfile.SyncAmbientLightSpeed;
    [ObservableProperty]
    private string cloudMusicVersion = string.Empty;
    [ObservableProperty]
    private string supportedVersion = CloudMusicLyricsReader.VersionResolverDictionary.Keys.FirstOrDefault() ?? string.Empty;
    public HaloPixelDevice Device { get; set; } = new();
    public CloudMusicLyricsReader Reader { get; set; }

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();

    /// <summary>
    /// Safely parse a hexadecimal address string to nint, returning 0 if parsing fails
    /// </summary>
    private static nint ParseHexAddress(string hexAddress)
    {
        if (string.IsNullOrWhiteSpace(hexAddress))
            return 0;

        try
        {
            return new nint(Convert.ToInt64(hexAddress, 16));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or ArgumentException)
        {
            Console.WriteLine($"[WARNING]无法解析内存地址 '{hexAddress}'，使用默认值 0。错误：{ex.Message}");
            return 0;
        }
    }

    private bool _forceRefresh;
    private bool _forcePauseLightRefresh;
    private bool _forcePauseScreenRefresh;
    private bool _forcePauseUIModelRefresh;
    private bool _isClockUI;

    public void OnNavigatedTo()
    {
        _forceRefresh = true;
    }

    public static (byte R, byte G, byte B) ParseHexColor(string hex)
    {
        try
        {
            if (hex.StartsWith("#"))
                hex = hex.Substring(1);
            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return (r, g, b);
            }
        }
        catch { }
        return (0xf0, 0xb4, 0xc8); // default fallback color
    }

    public void ForcePauseLightRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseLightRefresh = true;
            Console.WriteLine("[DEBUG] NetEase ForcePauseLightRefresh invoked");
        }
    }

    public void ForcePauseScreenRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseScreenRefresh = true;
            Console.WriteLine("[DEBUG] NetEase ForcePauseScreenRefresh invoked");
        }
    }

    public void ForcePauseUIModelRefresh()
    {
        if (_isClockUI)
        {
            _forcePauseUIModelRefresh = true;
            Console.WriteLine("[DEBUG] NetEase ForcePauseUIModelRefresh invoked");
        }
    }

    partial void OnEnableCloudMusicLyricsChanged(bool value)
    {
        CloudMusicLyricsProfile.EnableCloudMusicLyrics = value;
        if (value)
            _forceRefresh = true;
    }

    partial void OnUseInputedAddressChanged(bool value)
    {
        CloudMusicLyricsProfile.UseInputedAddress = value;
        Reader.UseInputedAddress = value;
    }

    partial void OnInputedAddressChanged(string value)
    {
        CloudMusicLyricsProfile.InputedAddress = value;
        Reader.Address = ParseHexAddress(value);
    }

    partial void OnSwitchBackWhenPauseChanged(bool value) => CloudMusicLyricsProfile.SwitchBackWhenPause = value;
    partial void OnSwitchBackTimeoutChanged(int value) => CloudMusicLyricsProfile.SwitchBackTimeout = value;

    partial void OnEnableScreenColorSyncChanged(bool value)
    {
        CloudMusicLyricsProfile.EnableScreenColorSync = value;
        if (!_isClockUI)
            _forceRefresh = true;
    }

    partial void OnEnableAmbientColorSyncChanged(bool value)
    {
        CloudMusicLyricsProfile.EnableAmbientColorSync = value;
        if (!_isClockUI)
            _forceRefresh = true;
    }

    partial void OnSyncAmbientLightEffectChanged(Core.Models.Lighting.AmbientLightEffect value)
    {
        CloudMusicLyricsProfile.SyncAmbientLightEffect = value;
        if (!_isClockUI)
            _forceRefresh = true;
    }

    partial void OnSyncAmbientLightBrightnessChanged(Core.Models.Lighting.AmbientLightBrightness value)
    {
        CloudMusicLyricsProfile.SyncAmbientLightBrightness = value;
        if (!_isClockUI)
            _forceRefresh = true;
    }

    partial void OnSyncAmbientLightSpeedChanged(int value)
    {
        CloudMusicLyricsProfile.SyncAmbientLightSpeed = value;
        if (!_isClockUI)
            _forceRefresh = true;
    }

    private Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _smtcManager;
    private Windows.Media.Control.GlobalSystemMediaTransportControlsSession? _cloudMusicSession;
    public (byte R, byte G, byte B)? CurrentAlbumColor { get; private set; }
    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;

    public bool IsPlaying
    {
        get
        {
            if (_cloudMusicSession == null) return false;
            try
            {
                var playbackInfo = _cloudMusicSession.GetPlaybackInfo();
                return playbackInfo?.PlaybackStatus == Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            }
            catch {}
            return false;
        }
    }

    public string CurrentTitle => _currentTitle;
    public string CurrentArtist => _currentArtist;

    private async Task InitSmtcAsync()
    {
        try
        {
            _smtcManager = await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            UpdateCloudMusicSession();
            _smtcManager.SessionsChanged += (s, e) => UpdateCloudMusicSession();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SMTC init failed: {ex.Message}");
        }
    }

    private void UpdateCloudMusicSession()
    {
        if (_smtcManager == null) return;
        var sessions = _smtcManager.GetSessions();
        var targetSession = sessions.FirstOrDefault(x => x.SourceAppUserModelId.Contains("cloudmusic", StringComparison.OrdinalIgnoreCase));
        if (targetSession != _cloudMusicSession)
        {
            if (_cloudMusicSession != null)
            {
                _cloudMusicSession.MediaPropertiesChanged -= OnSmtcMediaPropertiesChanged;
            }
            _cloudMusicSession = targetSession;
            if (_cloudMusicSession != null)
            {
                _cloudMusicSession.MediaPropertiesChanged += OnSmtcMediaPropertiesChanged;
                _ = UpdateAlbumColorAsync();
            }
        }
    }

    private void OnSmtcMediaPropertiesChanged(Windows.Media.Control.GlobalSystemMediaTransportControlsSession sender, Windows.Media.Control.MediaPropertiesChangedEventArgs args)
    {
        _ = UpdateAlbumColorAsync();
    }

    private async Task UpdateAlbumColorAsync()
    {
        if (_cloudMusicSession == null) return;
        try
        {
            var media = await _cloudMusicSession.TryGetMediaPropertiesAsync();
            if (media != null)
            {
                _currentTitle = media.Title ?? string.Empty;
                _currentArtist = media.Artist ?? string.Empty;
                if (media.Thumbnail != null)
                {
                    var color = await SpotifyLyricsReader.GetDominantColorAsync(media.Thumbnail);
                    if (color.HasValue)
                    {
                        CurrentAlbumColor = color.Value;
                        Console.WriteLine($"CloudMusic extracted album color: RGB({color.Value.R}, {color.Value.G}, {color.Value.B})");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to update CloudMusic album color: {ex.Message}");
        }
    }

    public CloudMusicLyricsToolPageViewModel()
    {
        _ = InitSmtcAsync();
        Console.WriteLine("初始化网易云歌词读取器");
        Reader = new CloudMusicLyricsReader
        {
            UseInputedAddress = UseInputedAddress,
            Address = ParseHexAddress(InputedAddress)
        };
        Console.WriteLine("准备启动网易云后台线程");
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("正在搜索花再设备...");
                while (!DeviceReady)
                {
                    var ready = Device.Initialize();
                    AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                        {
                            DeviceReady = ready;
                        });
                    await Task.Delay(500);
                }
                Console.WriteLine("花再设备已连接");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]搜索花再设备时发生错误：{ex.Message}");
                Console.WriteLine($"[TRACE]{ex.StackTrace}");
            }

        });
        Task.Run(async () =>
        {
            try
            {
                Console.WriteLine("正在搜索云音乐...");
                while (!CloudMusicReady)
                {
                    var ready = Reader.Initialize();
                    AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                    {
                        CloudMusicReady = ready;
                        CloudMusicVersion = Reader.VersionInfo is not null ? $"{Reader.VersionInfo.FileVersion}" : "未检测到云音乐";
                    });
                    await Task.Delay(500);
                }
                Console.WriteLine("云音乐已准备就绪");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]搜索云音乐时发生错误：{ex.Message}");
                Console.WriteLine($"[TRACE]{ex.StackTrace}");
            }
        });
        Task.Run(async () =>
        {
            Console.WriteLine("启动云音乐地址重解析线程");
            while (true)
            {
                try
                {
                    Reader.ReresolveAddress();
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]云音乐地址重解析线程发生错误：{ex.Message}");
                    Console.WriteLine($"[TRACE]{ex.StackTrace}");
                }
            }
        });
        Task.Run(async () =>
        {
            Console.WriteLine("启动网易云歌词主线程");
            Console.WriteLine("等待花再设备...");
            while (!DeviceReady)
                await Task.Delay(500);
            if (DeviceReady && EnableCloudMusicLyrics)
            {
                Console.WriteLine("花再设备已就绪，显示启动信息");
                Device.SetTextLayout(Core.Models.HaloPixelTextLayout.Center);
                Device.ShowText("花再工具箱已启动~");
                await Task.Delay(3000);
                Console.WriteLine("OK");
            }
            while (true)
            {
                _isClockUI = false;
                int time = 0;
                try
                {
                    if (DeviceReady && CloudMusicReady && EnableCloudMusicLyrics)
                    {
                        Console.WriteLine("[DEBUG]设备均在线，准备进入主循环");
                        string lastRead = string.Empty;
                        string lastTrackTitle = string.Empty;
                        string lastTrackArtist = string.Empty;
                        bool scrolled = false;
                        bool wasPlaying = false;
                        (byte R, byte G, byte B) lastScreenColor = (0, 0, 0);
                        (byte R, byte G, byte B) lastAmbientColor = (0, 0, 0);
                        while (true)
                        {
                            try
                            {
                                if (!DeviceReady || !CloudMusicReady || !EnableCloudMusicLyrics)
                                    break;

                                bool isPlaying = IsPlaying;
                                bool trackChanged = lastTrackTitle != CurrentTitle || lastTrackArtist != CurrentArtist;
                                bool lyricsChanged = Reader.TryReadLyrics(out var lyrics) && lastRead != lyrics;

                                if (isPlaying && (_isClockUI || trackChanged))
                                {
                                    if (!wasPlaying && isPlaying)
                                    {
                                        _isClockUI = false;
                                        _forceRefresh = true;
                                        time = 0;
                                    }
                                    else if (lyricsChanged || trackChanged)
                                    {
                                        _isClockUI = false;
                                        _forceRefresh = true;
                                        time = 0;
                                    }
                                }
                                wasPlaying = isPlaying;
                                lastTrackTitle = CurrentTitle ?? string.Empty;
                                lastTrackArtist = CurrentArtist ?? string.Empty;

                                var albumColor = CurrentAlbumColor;

                                // 1. Determine screen text color
                                (byte R, byte G, byte B) screenColor = ((byte)0xf0, (byte)0xb4, (byte)0xc8);
                                if (EnableScreenColorSync && albumColor.HasValue)
                                {
                                    screenColor = albumColor.Value;
                                }

                                // 2. Determine ambient backlight color
                                (byte R, byte G, byte B) ambientColor = ((byte)0xf0, (byte)0xb4, (byte)0xc8);
                                if (EnableAmbientColorSync && albumColor.HasValue)
                                {
                                    ambientColor = albumColor.Value;
                                }

                                bool colorChanged = lastScreenColor != screenColor || lastAmbientColor != ambientColor;

                                if (!_isClockUI && (lyricsChanged || colorChanged || _forceRefresh))
                                {
                                    _forceRefresh = false;
                                    lastScreenColor = screenColor;
                                    lastAmbientColor = ambientColor;
                                    HidPacketBuilder.CurrentColor = screenColor;

                                    try
                                    {
                                        Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = Core.Models.Lighting.AmbientLightEffect.Static,
                                            Color = new Core.Models.Display.HaloPixelColor(ambientColor.R, ambientColor.G, ambientColor.B),
                                            Brightness = Core.Models.Lighting.AmbientLightBrightness.High,
                                            Speed = 5
                                        });
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetAmbientLight failed: {ex.Message}");
                                    }

                                    try
                                    {
                                        Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(screenColor.R, screenColor.G, screenColor.B));
                                    }
                                    catch (Exception ex)
                                    {
                                        Console.WriteLine($"[ERROR] SetPixelScreenColor failed: {ex.Message}");
                                    }

                                    if (lyricsChanged)
                                    {
                                        Console.WriteLine($"已读取到歌词：{lyrics}");
                                        lastRead = lyrics;
                                        if (_isClockUI)
                                        {
                                            _isClockUI = false;
                                            _forceRefresh = true;
                                        }
                                        time = 0;
                                        if (scrolled)
                                        {
                                            Device.ShowText(string.Empty);
                                            await Task.Delay(100);
                                            scrolled = false;
                                        }
                                    }

                                    Device.SetTextLayout(CloudMusicLyricsProfile.DefaultHaloPixelTextLayout);
                                    Device.ShowText(lastRead);
                                    
                                    if (lyricsChanged && lastRead.DisplayLength() > 30)
                                    {
                                        scrolled = true;
                                        await Task.Delay(500);
                                        Device.SetTextLayout(Core.Models.HaloPixelTextLayout.ScrollRightToLeft);
                                    }
                                }
                                await Task.Delay(50);
                                time += 50;
                                if (!_isClockUI && time >= CloudMusicLyricsProfile.SwitchBackTimeout * 1000)
                                {
                                    _isClockUI = true;
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(CloudMusicLyricsProfile.DefaultScreenColor);
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(CloudMusicLyricsProfile.DefaultAmbientLightColor);
                                    HidPacketBuilder.CurrentColor = finalScreenColor;
                                    try
                                    {
                                        Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = CloudMusicLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = CloudMusicLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)CloudMusicLyricsProfile.DefaultAmbientLightSpeed
                                        });
                                    }
                                    catch {}
                                    try
                                    {
                                        Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B));
                                    }
                                    catch {}
                                    Device.SetUIModel(CloudMusicLyricsProfile.DefaultHaloPixelUIModel);
                                    Console.WriteLine("已切换至时钟界面");
                                }

                                if (_isClockUI && _forcePauseLightRefresh)
                                {
                                    _forcePauseLightRefresh = false;
                                    (byte R, byte G, byte B) finalAmbientColor = ParseHexColor(CloudMusicLyricsProfile.DefaultAmbientLightColor);
                                    try
                                    {
                                        Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = CloudMusicLyricsProfile.DefaultAmbientLightEffect,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = CloudMusicLyricsProfile.DefaultAmbientLightBrightness,
                                            Speed = (byte)CloudMusicLyricsProfile.DefaultAmbientLightSpeed
                                        });
                                    }
                                    catch {}
                                }

                                if (_isClockUI && _forcePauseScreenRefresh)
                                {
                                    _forcePauseScreenRefresh = false;
                                    (byte R, byte G, byte B) finalScreenColor = ParseHexColor(CloudMusicLyricsProfile.DefaultScreenColor);
                                    HidPacketBuilder.CurrentColor = finalScreenColor;
                                    try
                                    {
                                        Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B));
                                    }
                                    catch {}
                                }

                                if (_isClockUI && _forcePauseUIModelRefresh)
                                {
                                    _forcePauseUIModelRefresh = false;
                                    try
                                    {
                                        Device.SetUIModel(CloudMusicLyricsProfile.DefaultHaloPixelUIModel);
                                    }
                                    catch {}
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR]网易云歌词主循环发生错误：{ex.Message}");
                                Console.WriteLine($"[TRACE]{ex.StackTrace}");
                            }
                        }
                        Console.WriteLine($"[DEBUG]主循环已退出");
                    }
                    Console.WriteLine($"[DEBUG]状态\t音响：{DeviceReady} 软件：{CloudMusicReady} 启用状态：{EnableCloudMusicLyrics}");
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]网易云歌词主线程发生错误：{ex.Message}");
                    Console.WriteLine($"[TRACE]{ex.StackTrace}");
                }
            }
        });
        Console.WriteLine("网易云后台线程启动完成");
    }
}