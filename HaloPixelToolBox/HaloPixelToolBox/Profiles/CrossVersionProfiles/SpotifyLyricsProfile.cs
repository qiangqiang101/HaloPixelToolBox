using HaloPixelToolBox.Core.Models;
using XFEExtension.NetCore.AutoConfig;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Profiles.CrossVersionProfiles;

public partial class SpotifyLyricsProfile : XFEProfile
{
    public SpotifyLyricsProfile() => ProfilePath = $@"{AppPathHelper.LocalProfile}\{nameof(SpotifyLyricsProfile)}";

    /// <summary>
    /// 切换回默认显示内容的时间
    /// </summary>
    [ProfileProperty]
    private int switchBackTimeout = 60;

    /// <summary>
    /// 是否启用Spotify歌词
    /// </summary>
    [ProfileProperty]
    private bool enableSpotifyLyrics = false;

    /// <summary>
    /// 当暂停时切换回默认显示内容
    /// </summary>
    [ProfileProperty]
    private bool switchBackWhenPause = true;

    /// <summary>
    /// 默认歌词显示布局
    /// </summary>
    [ProfileProperty]
    private HaloPixelTextLayout defaultHaloPixelTextLayout = HaloPixelTextLayout.Center;

    /// <summary>
    /// 默认暂停界面
    /// </summary>
    [ProfileProperty]
    private HaloPixelUIModel defaultHaloPixelUIModel = HaloPixelUIModel.Clock;

    /// <summary>
    /// 是否开启屏幕颜色同步
    /// </summary>
    [ProfileProperty]
    private bool enableScreenColorSync = false;

    /// <summary>
    /// 是否开启氛围灯颜色同步
    /// </summary>
    [ProfileProperty]
    private bool enableAmbientColorSync = false;
}
