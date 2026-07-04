using CommunityToolkit.Mvvm.ComponentModel;
using HaloPixelToolBox.Core.Utilities;
using HaloPixelToolBox.Profiles.CrossVersionProfiles;
using HaloPixelToolBox.Utilities;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using XFEExtension.NetCore.StringExtension;
using XFEExtension.NetCore.WinUIHelper.Implements;
using XFEExtension.NetCore.WinUIHelper.Interface.Services;
using XFEExtension.NetCore.WinUIHelper.Utilities;

namespace HaloPixelToolBox.ViewModels;

public partial class SpotifyLyricsToolPageViewModel : ServiceBaseViewModelBase<string>
{
    [ObservableProperty]
    private bool deviceReady;
    [ObservableProperty]
    private bool spotifyReady;
    [ObservableProperty]
    private bool enableSpotifyLyrics = SpotifyLyricsProfile.EnableSpotifyLyrics;
    [ObservableProperty]
    private bool switchBackWhenPause = SpotifyLyricsProfile.SwitchBackWhenPause;
    [ObservableProperty]
    private int switchBackTimeout = SpotifyLyricsProfile.SwitchBackTimeout;
    [ObservableProperty]
    private bool enableScreenColorSync = SpotifyLyricsProfile.EnableScreenColorSync;
    [ObservableProperty]
    private bool enableAmbientColorSync = SpotifyLyricsProfile.EnableAmbientColorSync;
    [ObservableProperty]
    private string spotifyTrackInfo = "未检测到播放中的歌曲";

    public HaloPixelDevice Device { get; set; } = new();
    public SpotifyLyricsReader Reader { get; set; }

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();

    private bool _forceRefresh;

    public void OnNavigatedTo()
    {
        _forceRefresh = true;
    }

    partial void OnEnableSpotifyLyricsChanged(bool value)
    {
        SpotifyLyricsProfile.EnableSpotifyLyrics = value;
        if (value)
            _forceRefresh = true;
    }
    partial void OnSwitchBackWhenPauseChanged(bool value) => SpotifyLyricsProfile.SwitchBackWhenPause = value;
    partial void OnSwitchBackTimeoutChanged(int value) => SpotifyLyricsProfile.SwitchBackTimeout = value;
    partial void OnEnableScreenColorSyncChanged(bool value)
    {
        SpotifyLyricsProfile.EnableScreenColorSync = value;
        _forceRefresh = true;
    }
    partial void OnEnableAmbientColorSyncChanged(bool value)
    {
        SpotifyLyricsProfile.EnableAmbientColorSync = value;
        _forceRefresh = true;
    }

    public SpotifyLyricsToolPageViewModel()
    {
        Console.WriteLine("初始化Spotify歌词读取器");
        Reader = new SpotifyLyricsReader();

        Console.WriteLine("准备启动Spotify后台线程");
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
                Console.WriteLine("正在搜索Spotify...");
                while (!SpotifyReady)
                {
                    var ready = Reader.Initialize();
                    AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                    {
                        SpotifyReady = ready;
                        SpotifyTrackInfo = !string.IsNullOrEmpty(Reader.CurrentTitle) ? $"{Reader.CurrentArtist} - {Reader.CurrentTitle}" : "未检测到播放中的歌曲";
                    });
                    await Task.Delay(500);
                }
                Console.WriteLine("Spotify已准备就绪");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]搜索Spotify时发生错误：{ex.Message}");
                Console.WriteLine($"[TRACE]{ex.StackTrace}");
            }
        });

        Task.Run(async () =>
        {
            Console.WriteLine("启动Spotify歌词主线程");
            Console.WriteLine("等待花再设备...");
            while (!DeviceReady)
                await Task.Delay(500);

            if (DeviceReady && EnableSpotifyLyrics)
            {
                Console.WriteLine("花再设备已就绪，显示启动信息");
                Device.SetTextLayout(Core.Models.HaloPixelTextLayout.Center);
                Device.ShowText("Spotify歌词同步已就绪");
                await Task.Delay(3000);
            }

            while (true)
            {
                bool isClockUI = false;
                int time = 0;
                try
                {
                    if (DeviceReady && SpotifyReady && EnableSpotifyLyrics)
                    {
                        Console.WriteLine("[DEBUG]设备均在线，准备进入主循环");
                        string lastRead = string.Empty;
                        bool scrolled = false;
                        (byte R, byte G, byte B) lastScreenColor = (0, 0, 0);
                        (byte R, byte G, byte B) lastAmbientColor = (0, 0, 0);
                        while (true)
                        {
                            try
                            {
                                if (!DeviceReady || !SpotifyReady || !EnableSpotifyLyrics)
                                    break;

                                // Update track info text in UI
                                AutoNavigationParameterService.CurrentPage?.DispatcherQueue.TryEnqueue(() =>
                                {
                                    SpotifyTrackInfo = !string.IsNullOrEmpty(Reader.CurrentTitle) ? $"{Reader.CurrentArtist} - {Reader.CurrentTitle}" : "无播放中的歌曲";
                                });

                                var albumColor = Reader.CurrentAlbumColor;

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
                                bool lyricsChanged = Reader.TryReadLyrics(out var lyrics) && lastRead != lyrics;

                                if (lyricsChanged || colorChanged || _forceRefresh)
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
                                        isClockUI = false;
                                        time = 0;
                                        if (scrolled)
                                        {
                                            Device.ShowText(string.Empty);
                                            await Task.Delay(100);
                                            scrolled = false;
                                        }
                                    }

                                    Device.SetTextLayout(SpotifyLyricsProfile.DefaultHaloPixelTextLayout);
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
                                if (!isClockUI && time >= SpotifyLyricsProfile.SwitchBackTimeout * 1000)
                                {
                                    isClockUI = true;
                                    (byte R, byte G, byte B) finalScreenColor = ((byte)0xf0, (byte)0xb4, (byte)0xc8);
                                    if (EnableScreenColorSync && Reader.CurrentAlbumColor.HasValue)
                                    {
                                        finalScreenColor = Reader.CurrentAlbumColor.Value;
                                    }
                                    (byte R, byte G, byte B) finalAmbientColor = ((byte)0xf0, (byte)0xb4, (byte)0xc8);
                                    if (EnableAmbientColorSync && Reader.CurrentAlbumColor.HasValue)
                                    {
                                        finalAmbientColor = Reader.CurrentAlbumColor.Value;
                                    }
                                    HidPacketBuilder.CurrentColor = finalScreenColor;
                                    try
                                    {
                                        Device.SetAmbientLight(new Core.Models.Lighting.AmbientLightOptions
                                        {
                                            Effect = Core.Models.Lighting.AmbientLightEffect.Static,
                                            Color = new Core.Models.Display.HaloPixelColor(finalAmbientColor.R, finalAmbientColor.G, finalAmbientColor.B),
                                            Brightness = Core.Models.Lighting.AmbientLightBrightness.High,
                                            Speed = 5
                                        });
                                    }
                                    catch {}
                                    try
                                    {
                                        Device.SetPixelScreenColor(new Core.Models.Display.HaloPixelColor(finalScreenColor.R, finalScreenColor.G, finalScreenColor.B));
                                    }
                                    catch {}
                                    Device.SetUIModel(SpotifyLyricsProfile.DefaultHaloPixelUIModel);
                                    Console.WriteLine("已切换至时钟界面");
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"[ERROR]Spotify歌词主循环发生错误：{ex.Message}");
                                Console.WriteLine($"[TRACE]{ex.StackTrace}");
                            }
                        }
                    }
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]Spotify歌词主线程发生错误：{ex.Message}");
                    Console.WriteLine($"[TRACE]{ex.StackTrace}");
                }
            }
        });
        Console.WriteLine("Spotify后台线程启动完成");
    }
}
