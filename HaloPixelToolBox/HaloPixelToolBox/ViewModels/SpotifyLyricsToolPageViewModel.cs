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
    private string spotifyTrackInfo = "未检测到播放中的歌曲";

    public HaloPixelDevice Device { get; set; } = new();
    public SpotifyLyricsReader Reader { get; set; }

    public ISettingService SettingService { get; } = ServiceManager.GetService<ISettingService>();

    partial void OnEnableSpotifyLyricsChanged(bool value) => SpotifyLyricsProfile.EnableSpotifyLyrics = value;
    partial void OnSwitchBackWhenPauseChanged(bool value) => SpotifyLyricsProfile.SwitchBackWhenPause = value;
    partial void OnSwitchBackTimeoutChanged(int value) => SpotifyLyricsProfile.SwitchBackTimeout = value;

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

            if (DeviceReady)
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

                                if (Reader.TryReadLyrics(out var lyrics) && lastRead != lyrics)
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
                                    Device.SetTextLayout(SpotifyLyricsProfile.DefaultHaloPixelTextLayout);
                                    Device.ShowText(lyrics);
                                    Debug.WriteLine(lyrics.DisplayLength());
                                    if (lyrics.DisplayLength() > 30)
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
