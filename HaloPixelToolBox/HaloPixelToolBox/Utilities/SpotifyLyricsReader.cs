using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace HaloPixelToolBox.Utilities;

public class LyricLine
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = string.Empty;
}

public static class LrcParser
{
    private static readonly Regex LrcTimeRegex = new Regex(@"\[(\d+):(\d+)(?:\.(\d+))?\]", RegexOptions.Compiled);

    public static List<LyricLine> Parse(string lrcContent)
    {
        var lines = new List<LyricLine>();
        if (string.IsNullOrWhiteSpace(lrcContent))
            return lines;

        var rawLines = lrcContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in rawLines)
        {
            var match = LrcTimeRegex.Match(line);
            if (match.Success)
            {
                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var fractionStr = match.Groups[3].Value;
                var milliseconds = 0;
                if (!string.IsNullOrEmpty(fractionStr))
                {
                    if (fractionStr.Length == 2)
                        milliseconds = int.Parse(fractionStr) * 10;
                    else if (fractionStr.Length == 3)
                        milliseconds = int.Parse(fractionStr);
                    else
                        milliseconds = int.Parse(fractionStr.Substring(0, 3));
                }

                var timestamp = new TimeSpan(0, 0, minutes, seconds, milliseconds);
                var text = line.Substring(match.Length).Trim();
                lines.Add(new LyricLine { Timestamp = timestamp, Text = text });
            }
        }
        lines.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return lines;
    }
}

public class LrclibResponse
{
    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }

    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }
}

public class SpotifyLyricsReader
{
    private static readonly HttpClient HttpClient = new HttpClient();

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    private string _currentTitle = string.Empty;
    private string _currentArtist = string.Empty;
    private string _currentAlbum = string.Empty;
    private double _currentDuration = 0;
    private List<LyricLine> _lyricLines = new();

    public string CurrentTitle => _currentTitle;
    public string CurrentArtist => _currentArtist;

    static SpotifyLyricsReader()
    {
        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("HaloPixelToolBox/1.2.7 (https://github.com/WePro/HaloPixelToolBox)");
    }

    public bool Initialize()
    {
        try
        {
            var task = Task.Run(async () =>
            {
                _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager.SessionsChanged += OnSessionsChanged;
                UpdateCurrentSession();
                return _currentSession != null;
            });
            return task.Result;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] SpotifyLyricsReader Initialize failed: {ex.Message}");
            return false;
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        UpdateCurrentSession();
    }

    private void UpdateCurrentSession()
    {
        if (_manager == null) return;

        var sessions = _manager.GetSessions();
        GlobalSystemMediaTransportControlsSession? spotifySession = null;
        foreach (var s in sessions)
        {
            if (s.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            {
                spotifySession = s;
                break;
            }
        }

        if (spotifySession != _currentSession)
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }

            _currentSession = spotifySession;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                _ = UpdateTrackAsync(_currentSession);
            }
            else
            {
                _currentTitle = string.Empty;
                _currentArtist = string.Empty;
                _currentAlbum = string.Empty;
                _currentDuration = 0;
                _lyricLines.Clear();
            }
        }
    }

    private async void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        await UpdateTrackAsync(sender);
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        // Playback status might have changed, could trigger UI updates
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        // Timeline properties updated
    }

    private async Task UpdateTrackAsync(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            if (media == null) return;

            var timeline = session.GetTimelineProperties();
            double duration = timeline?.EndTime.TotalSeconds ?? 0;

            if (_currentTitle == media.Title && _currentArtist == media.Artist)
            {
                return; // Same track
            }

            _currentTitle = media.Title;
            _currentArtist = media.Artist;
            _currentAlbum = media.AlbumTitle;
            _currentDuration = duration;
            _lyricLines.Clear();

            Console.WriteLine($"[SpotifyLyricsReader] Track changed: {_currentArtist} - {_currentTitle} ({_currentAlbum}, {duration}s)");

            var lyricsRes = await FetchLyricsAsync(_currentTitle, _currentArtist, _currentAlbum, _currentDuration);
            if (lyricsRes != null && !string.IsNullOrEmpty(lyricsRes.SyncedLyrics))
            {
                _lyricLines = LrcParser.Parse(lyricsRes.SyncedLyrics);
                Console.WriteLine($"[SpotifyLyricsReader] Loaded {_lyricLines.Count} synced lines.");
            }
            else if (lyricsRes != null && !string.IsNullOrEmpty(lyricsRes.PlainLyrics))
            {
                Console.WriteLine("[SpotifyLyricsReader] No synced lyrics, plain lyrics available.");
            }
            else
            {
                Console.WriteLine("[SpotifyLyricsReader] No lyrics found.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] UpdateTrackAsync failed: {ex.Message}");
        }
    }

    private async Task<LrclibResponse?> FetchLyricsAsync(string title, string artist, string album, double duration)
    {
        try
        {
            string url = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            if (!string.IsNullOrEmpty(album))
                url += $"&album_name={Uri.EscapeDataString(album)}";
            if (duration > 0)
                url += $"&duration={(int)duration}";

            var response = await HttpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<LrclibResponse>();
            }

            // Fallback: search without album/duration if direct match fails
            if (!string.IsNullOrEmpty(album) || duration > 0)
            {
                string fallbackUrl = $"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
                var fallbackRes = await HttpClient.GetAsync(fallbackUrl);
                if (fallbackRes.IsSuccessStatusCode)
                {
                    return await fallbackRes.Content.ReadFromJsonAsync<LrclibResponse>();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ERROR] FetchLyricsAsync failed: {ex.Message}");
        }
        return null;
    }

    public bool TryReadLyrics(out string lyrics)
    {
        lyrics = string.Empty;
        if (_currentSession == null)
        {
            lyrics = "Spotify未就绪";
            return false;
        }

        var playback = _currentSession.GetPlaybackInfo();
        if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed ||
            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
        {
            lyrics = "播放已停止";
            return false;
        }

        if (_lyricLines == null || _lyricLines.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(_currentTitle))
            {
                lyrics = $"{_currentArtist} - {_currentTitle}";
                return true;
            }
            lyrics = "无歌词信息";
            return false;
        }

        var timeline = _currentSession.GetTimelineProperties();
        if (timeline == null)
        {
            lyrics = $"{_currentArtist} - {_currentTitle}";
            return true;
        }

        TimeSpan currentPosition;
        if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            var elapsed = DateTimeOffset.Now - timeline.LastUpdatedTime;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            currentPosition = timeline.Position + elapsed;
        }
        else
        {
            currentPosition = timeline.Position;
        }

        string currentText = string.Empty;
        foreach (var line in _lyricLines)
        {
            if (line.Timestamp <= currentPosition)
            {
                currentText = line.Text;
            }
            else
            {
                break;
            }
        }

        if (string.IsNullOrEmpty(currentText))
        {
            lyrics = $"{_currentArtist} - {_currentTitle}";
        }
        else
        {
            lyrics = currentText;
        }

        return true;
    }
}
