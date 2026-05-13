using Microsoft.Maui.Graphics;
using Microsoft.Maui.Storage;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using Ardin.BoomDaad.Services;

namespace Ardin.BoomDaad;

public class PlayerScore
{
    public string Name { get; set; }
    public double Time { get; set; }
}

public partial class MainPage : ContentPage
{
    private readonly IAudioLevelService _audioService;
    private Stopwatch _stopwatch = new Stopwatch();
    private bool _isWaitingForGreen = false;
    private bool _isGreenGoState = false;
    private bool _isInCalibrationMode = false;
    private IDispatcherTimer _uiTimer;

    // --- تنظیمات بازی چند نفره ---
    private List<string> _players = new List<string>();
    private List<PlayerScore> _scores = new List<PlayerScore>();
    private int _currentPlayerIndex = 0;

    // --- تنظیمات صدای داینامیک ---
    private double _ambientNoiseLevel = 30.0; // صدای محیط (به صورت پیش‌فرض 30)
    private double _maxShoutLevel = 35.0;     // بالاترین دادی که ثبت شده
    private double _dynamicShoutThreshold = 50.0; // آستانه نهایی برای قبولی داد (محاسبه خودکار)
    private bool _calibrationReady = false;   // whether a good shout has been recorded

    private const string PlayersPrefsKey = "saved_players";

    public MainPage(IAudioLevelService audioService)
    {
        InitializeComponent();
        _audioService = audioService;

        AppInfoLabel.Text = $"نسخه {AppInfo.VersionString} | توسعه‌دهنده: Ardin";

        _uiTimer = Dispatcher.CreateTimer();
        _uiTimer.Interval = TimeSpan.FromMilliseconds(50);
        _uiTimer.Tick += (s, e) =>
        {
            if (_stopwatch.IsRunning)
                TimerLabel.Text = $"{(int)_stopwatch.Elapsed.TotalSeconds:D2}.{_stopwatch.Elapsed.Milliseconds:D3}";
        };

        SetBackgroundColor(Colors.White);
    }

    private void SetBackgroundColor(Color color)
    {
        this.BackgroundColor = color;
    }

    private void HideAllScreens()
    {
        StartScreen.IsVisible = false;
        PlayerSetupScreen.IsVisible = false;
        CalibrationScreen.IsVisible = false;
        TurnScreen.IsVisible = false;
        GameScreen.IsVisible = false;
        ResultScreen.IsVisible = false;
        LeaderboardScreen.IsVisible = false;
        HelpScreen.IsVisible = false;
    }

    // 1. کلیک روی شروع بازی در صفحه اول
    private void OnStartClicked(object sender, EventArgs e)
    {
        HideAllScreens();
        _scores.Clear();
        LoadSavedPlayers();
        PlayerSetupScreen.IsVisible = true;
    }

    private void LoadSavedPlayers()
    {
        string saved = Preferences.Get(PlayersPrefsKey, string.Empty);
        _players.Clear();
        if (!string.IsNullOrEmpty(saved))
        {
            var names = saved.Split(';', System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var n in names)
            {
                string trimmed = n.Trim();
                if (!string.IsNullOrEmpty(trimmed) && !_players.Contains(trimmed))
                    _players.Add(trimmed);
            }
        }
        _currentPlayerIndex = 0;
        UpdatePlayersListUI();
    }

    private void SavePlayers()
    {
        string joined = string.Join(";", _players.Select(p => p.Trim()));
        Preferences.Set(PlayersPrefsKey, joined);
    }

    // 2. اضافه کردن بازیکن
    private void OnAddPlayerClicked(object sender, EventArgs e)
    {
        string name = PlayerNameEntry.Text?.Trim();
        if (!string.IsNullOrEmpty(name) && !_players.Contains(name))
        {
            _players.Add(name);
            PlayerNameEntry.Text = "";
            SavePlayers();
            UpdatePlayersListUI();
        }
    }

    private void UpdatePlayersListUI()
    {
        if (_players.Count == 0)
            PlayersListLabel.Text = "لیست: خالی";
        else
            PlayersListLabel.Text = "بازیکن‌ها: " + string.Join("، ", _players);
    }

    // 3. رفتن به صفحه کالیبره
    private async void OnGoToCalibrationClicked(object sender, EventArgs e)
    {
        if (_players.Count == 0)
        {
            await DisplayAlert("خطا", "حداقل یک بازیکن باید اضافه بشه!", "باشه");
            return;
        }

        var status = await Permissions.RequestAsync<Permissions.Microphone>();
        if (status != PermissionStatus.Granted)
        {
            await DisplayAlert("خطا", "بدون دسترسی میکروفون نمیشه بازی کرد!", "باشه");
            return;
        }

        HideAllScreens();
        CalibrationScreen.IsVisible = true;
        SetBackgroundColor(Colors.White);

        // reset calibration state
        _isInCalibrationMode = true;
        _calibrationReady = false;
        CalibrationDoneButton.IsEnabled = false;
        CalibrationDoneButton.BackgroundColor = Color.FromArgb("#2196F3"); // back to original blue

        _audioService.OnLevelChanged -= OnAudioLevelChanged;
        _audioService.OnLevelChanged += OnAudioLevelChanged;

        // ریست کردن مقادیر برای کالیبره جدید
        _maxShoutLevel = 0;
        _ambientNoiseLevel = 1000; // یک عدد بالا میدیم تا با اولین سکوت بیاد پایین
        MicLevelBar.Progress = 0;
        CalibrationStatusLabel.Text = "اول هیچی نگو، بعد یه داد بلند بزن!";
        CalibrationStatusLabel.TextColor = Color.FromArgb("#FF9800");

        _audioService.Start();
    }

    // 4. اتمام کالیبره و رفتن به نفر اول
    private async void OnCalibrationDoneClicked(object sender, EventArgs e)
    {
        if (!_calibrationReady)
        {
            await DisplayAlert("هشدار", "لطفاً اول یه داد بلند بزن!", "باشه");
            return;
        }

        _isInCalibrationMode = false;
        _audioService.Stop(); // موقتا میکروفون خاموش بشه

        ShufflePlayers(); // random order each match

        _currentPlayerIndex = 0;
        ShowTurnScreen();
    }

    private void ShufflePlayers()
    {
        var rng = new System.Random();
        int n = _players.Count;
        while (n > 1)
        {
            n--;
            int k = rng.Next(n + 1);
            (_players[k], _players[n]) = (_players[n], _players[k]);
        }
    }

    // 5. نمایش صفحه اعلام نوبت
    private void ShowTurnScreen()
    {
        HideAllScreens();
        TurnScreen.IsVisible = true;
        SetBackgroundColor(Colors.White);

        string currentPlayer = _players[_currentPlayerIndex];
        TurnLabel.Text = $"نوبت: {currentPlayer} 🎮";
    }

    // 6. بازیکن آماده است، شروع راند
    private void OnReadyForTurnClicked(object sender, EventArgs e)
    {
        HideAllScreens();
        GameScreen.IsVisible = true;
        StartGameRound();
    }

    private async void StartGameRound()
    {
        TimerLabel.Text = "00.000";
        _stopwatch.Reset();

        SetBackgroundColor(Colors.White);
        SignalFrame.BackgroundColor = Color.FromArgb("#FF9800");
        GameStatusLabel.Text = "آماده باش...";
        _isWaitingForGreen = true;
        _isGreenGoState = false;

        _audioService.OnLevelChanged -= OnAudioLevelChanged;
        _audioService.OnLevelChanged += OnAudioLevelChanged;
        _audioService.Start();

        int delayMs = new System.Random().Next(2000, 5000);
        await Task.Delay(delayMs);

        if (_isWaitingForGreen)
        {
            _isWaitingForGreen = false;
            _isGreenGoState = true;

            SignalFrame.BackgroundColor = Color.FromArgb("#4CAF50");
            GameStatusLabel.Text = "حالا داد بزن!!!";

            _stopwatch.Start();
            _uiTimer.Start();
        }
    }

    private void OnAudioLevelChanged(double currentLevel)
    {
        Dispatcher.Dispatch(() =>
        {
            if (_isInCalibrationMode)
            {
                // 1. پیدا کردن صدای محیط (کمترین صدایی که میکروفون میشنوه)
                if (currentLevel < _ambientNoiseLevel && currentLevel > 0)
                {
                    _ambientNoiseLevel = currentLevel;
                }

                // 2. پیدا کردن بالاترین صدای داد زدن
                if (currentLevel > _maxShoutLevel)
                {
                    _maxShoutLevel = currentLevel;

                    // محاسبه هوشمند آستانه: 
                    double difference = _maxShoutLevel - _ambientNoiseLevel;
                    _dynamicShoutThreshold = _ambientNoiseLevel + (difference * 0.7);
                }

                // نمایش اعداد برای دیباگ
                RawDbLabel.Text = $"صدا الان: {currentLevel:F0} | محیط: {_ambientNoiseLevel:F0} | رکورد داد: {_maxShoutLevel:F0}";

                // 3. پر کردن پروگرس بار
                double range = _maxShoutLevel - _ambientNoiseLevel;
                if (range < 5) range = 5;

                double progress = (currentLevel - _ambientNoiseLevel) / range;
                MicLevelBar.Progress = Math.Clamp(progress, 0, 1);

                // 4. بازخورد بصری – همچنین بررسی اینکه کالیبره انجام شده
                if (currentLevel >= _dynamicShoutThreshold && _maxShoutLevel > _ambientNoiseLevel + 15)
                {
                    MicLevelBar.ProgressColor = Color.FromArgb("#F44336"); // قرمز: داد
                    CalibrationStatusLabel.Text = "عالیه! سیستمت کالیبره شد 🔊";
                    CalibrationStatusLabel.TextColor = Color.FromArgb("#D32F2F");

                    if (!_calibrationReady)
                    {
                        _calibrationReady = true;
                        CalibrationDoneButton.IsEnabled = true;
                        CalibrationDoneButton.BackgroundColor = Color.FromArgb("#4CAF50"); // سبز : فعال
                    }
                }
                else if (currentLevel > _ambientNoiseLevel + 10)
                {
                    MicLevelBar.ProgressColor = Color.FromArgb("#F57C00"); // نارنجی: متوسط
                    CalibrationStatusLabel.Text = "بلندتر... 🔉";
                    CalibrationStatusLabel.TextColor = Color.FromArgb("#F57C00");
                }
                else
                {
                    MicLevelBar.ProgressColor = Color.FromArgb("#4CAF50"); // سبز: پایین
                }

                return;
            }

            // --- منطق اصلی بازی حین مسابقه ---
            if (currentLevel >= _dynamicShoutThreshold)
            {
                if (_isWaitingForGreen)
                {
                    GameOver(false); // داد زدن زودتر از موعد
                }
                else if (_isGreenGoState)
                {
                    GameOver(true); // برنده شد
                }
            }
        });
    }

    private void GameOver(bool isWinner)
    {
        _isWaitingForGreen = false;
        _isGreenGoState = false;
        _stopwatch.Stop();
        _uiTimer.Stop();
        _audioService.OnLevelChanged -= OnAudioLevelChanged;
        _audioService.Stop();

        HideAllScreens();
        ResultScreen.IsVisible = true;

        string currentPlayer = _players[_currentPlayerIndex];

        if (isWinner)
        {
            double finalTime = _stopwatch.Elapsed.TotalSeconds;

            // ذخیره رکورد
            _scores.Add(new PlayerScore { Name = currentPlayer, Time = finalTime });

            SetBackgroundColor(Colors.White);
            ResultIconLabel.Text = "🎯";
            ResultTitleLabel.Text = $"عالی بود {currentPlayer}!";
            ResultTitleLabel.TextColor = Color.FromArgb("#212121");
            ResultTimeLabel.Text = $"زمان شما: {(int)_stopwatch.Elapsed.TotalSeconds:D2}.{_stopwatch.Elapsed.Milliseconds:D3} ثانیه";

            // تصمیم برای دکمه (نفر بعدی یا لیدربورد)
            if (_currentPlayerIndex < _players.Count - 1)
                NextStepButton.Text = "بریم نفر بعدی";
            else
                NextStepButton.Text = "مشاهده جدول رده‌بندی!";
        }
        else
        {
            SetBackgroundColor(Colors.White);
            ResultIconLabel.Text = "❌";
            ResultTitleLabel.Text = $"سوختی {currentPlayer}!";
            ResultTitleLabel.TextColor = Color.FromArgb("#D32F2F");
            ResultTimeLabel.Text = "خیلی زود داد زدی! یکبار دیگه تلاش کن.";
            NextStepButton.Text = "تلاش مجدد";
            try { Vibration.Default.Vibrate(TimeSpan.FromMilliseconds(500)); } catch { }
        }
    }

    // کلیک روی دکمه در صفحه نتیجه
    private void OnNextStepClicked(object sender, EventArgs e)
    {
        if (NextStepButton.Text == "تلاش مجدد")
        {
            ShowTurnScreen();
        }
        else if (NextStepButton.Text == "بریم نفر بعدی")
        {
            _currentPlayerIndex++;
            ShowTurnScreen();
        }
        else
        {
            ShowLeaderboard();
        }
    }

    private void ShowLeaderboard()
    {
        HideAllScreens();
        LeaderboardScreen.IsVisible = true;
        SetBackgroundColor(Colors.White);

        LeaderboardList.Children.Clear();

        // مرتب‌سازی رکوردها از کمترین زمان به بیشترین
        var sortedScores = _scores.OrderBy(s => s.Time).ToList();

        for (int i = 0; i < sortedScores.Count; i++)
        {
            string medal = i switch
            {
                0 => "🥇",
                1 => "🥈",
                2 => "🥉",
                _ => "👤"
            };

            var scoreLabel = new Label
            {
                Text = $"{medal} رتبه {i + 1}: {sortedScores[i].Name} - {sortedScores[i].Time:F3} ثانیه",
                FontSize = 22,
                TextColor = (i == 0) ? Color.FromArgb("#F9A825") : Color.FromArgb("#212121"),
                HorizontalOptions = LayoutOptions.Center
            };

            LeaderboardList.Children.Add(scoreLabel);
        }
    }

    private void OnPlayAgainClicked(object sender, EventArgs e)
    {
        HideAllScreens();
        StartScreen.IsVisible = true;
        SetBackgroundColor(Colors.White);
    }

    // --- Help screen handlers ---
    private void OnHelpClicked(object sender, EventArgs e)
    {
        HideAllScreens();
        HelpScreen.IsVisible = true;
        SetBackgroundColor(Colors.White);
    }

    private void OnBackFromHelpClicked(object sender, EventArgs e)
    {
        HideAllScreens();
        StartScreen.IsVisible = true;
        SetBackgroundColor(Colors.White);
    }
}
