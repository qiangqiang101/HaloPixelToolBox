using HaloPixelToolBox.Core.Models;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using XFEExtension.NetCore.WinUIHelper.Utilities.Helper;

namespace HaloPixelToolBox.Views;

public sealed partial class SpotifyLyricsToolPage : Page
{
    public static SpotifyLyricsToolPage? Current { get; set; }
    public SpotifyLyricsToolPageViewModel ViewModel { get; set; } = new();

    public SpotifyLyricsToolPage()
    {
        Console.WriteLine("正在初始化Spotify歌词界面...");
        Current = this;
        InitializeComponent();
        ViewModel.AutoNavigationParameterService.Initialize(this);
        ViewModel.SettingService.AddComboBox(defaultHaloPixelTextLayoutComboBox, ProfileHelper.GetEnumProfileSaveFunc<HaloPixelTextLayout>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
        ViewModel.SettingService.AddComboBox(defaultHaloPixelUIModelComboBox, ProfileHelper.GetEnumProfileSaveFunc<HaloPixelUIModel>(), ProfileHelper.GetEnumProfileLoadFuncForComboBox());
        ViewModel.SettingService.Initialize();
        ViewModel.SettingService.RegisterEvents();
        NavigationCacheMode = NavigationCacheMode.Enabled;
        Console.WriteLine("Spotify歌词界面初始化完成");
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        Console.WriteLine("导航到Spotify歌词页面");
        ViewModel.AutoNavigationParameterService.Initialize(this);
        ViewModel.AutoNavigationParameterService.OnParameterChange(e.Parameter);
        ViewModel.OnNavigatedTo();
    }
}
