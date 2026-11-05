using System.Diagnostics;
using System.Globalization;
using System.Media;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;

namespace CoinFlip;

public partial class MainWindow : Window
{
    private enum CoinSide
    {
        Heads,
        Tails
    }

    private readonly DispatcherTimer autoFlipTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer animationTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly string soundPath;
    private readonly SoundPlayer? windowsSoundPlayer;
    private CoinSide currentSide = CoinSide.Heads;
    private CoinSide targetSide;
    private int headsScore;
    private int tailsScore;
    private bool seriesOver;
    private bool isFlipping;
    private double flipProgress;

    public MainWindow()
    {
        InitializeComponent();

        soundPath = ExtractSound();
        if (OperatingSystem.IsWindows())
        {
            windowsSoundPlayer = new SoundPlayer(soundPath);
        }
        autoFlipTimer.Tick += (_, _) => StartFlip();
        animationTimer.Tick += AnimationTimer_Tick;
        coinCanvas.RenderStateChanged += (_, _) => coinCanvas.InvalidateVisual();
        bestOfNumericUpDown.PropertyChanged += (_, e) =>
        {
            if (e.Property == NumericUpDown.ValueProperty)
            {
                StopAutoFlip();
                ResetSeries();
            }
        };
        soundCheckBox.PropertyChanged += (_, e) =>
        {
            if (e.Property == CheckBox.IsCheckedProperty)
            {
                soundCheckBox.Content = soundCheckBox.IsChecked == true ? "Enable Sound" : "Sound Disabled";
            }
        };
        resetButton.Click += (_, _) => ResetButton_Click();
        flipButton.Click += (_, _) => FlipButton_Click();

        ResetSeries();
    }

    private int WinsNeeded => ((int)(bestOfNumericUpDown.Value ?? 3) + 1) / 2;

    private void FlipButton_Click()
    {
        if (autoFlipTimer.IsEnabled)
        {
            StopAutoFlip();
        }
        else
        {
            StartAutoFlip();
            StartFlip();
        }
    }

    private void ResetButton_Click()
    {
        StopAutoFlip();
        StopAnimation();
        currentSide = CoinSide.Heads;
        coinCanvas.Side = CoinSideState.Heads;
        ResetSeries();
        coinCanvas.InvalidateVisual();
    }

    private void StartAutoFlip()
    {
        if (seriesOver)
        {
            ResetSeries();
        }

        autoFlipTimer.Start();
        flipButton.Content = "Stop";
    }

    private void StopAutoFlip()
    {
        autoFlipTimer.Stop();
        flipButton.Content = "Flip";
    }

    private void StopAnimation()
    {
        animationTimer.Stop();
        isFlipping = false;
        flipProgress = 0;
    }

    private void ResetSeries()
    {
        headsScore = 0;
        tailsScore = 0;
        seriesOver = false;
        resultLabel.Text = "Press Flip";
        UpdateScoreLabel();
    }

    private void StartFlip()
    {
        if (isFlipping)
        {
            return;
        }

        if (seriesOver)
        {
            ResetSeries();
        }

        targetSide = Random.Shared.Next(2) == 0 ? CoinSide.Tails : CoinSide.Heads;
        if (targetSide == CoinSide.Heads)
        {
            headsScore++;
        }
        else
        {
            tailsScore++;
        }

        UpdateScoreLabel();
        resultLabel.Text = "Flipping...";
        isFlipping = true;
        flipProgress = 0;
        animationTimer.Start();
        coinCanvas.InvalidateVisual();
        PlaySound();
    }

    private void AnimationTimer_Tick(object? sender, EventArgs e)
    {
        flipProgress += animationTimer.Interval.TotalMilliseconds / 300.0;
        currentSide = ((int)(flipProgress * 6) % 2 == 0) ? targetSide : Opposite(targetSide);
        coinCanvas.Side = currentSide == CoinSide.Heads ? CoinSideState.Heads : CoinSideState.Tails;
        coinCanvas.SpinProgress = flipProgress;

        if (flipProgress >= 1)
        {
            flipProgress = 1;
            currentSide = targetSide;
            StopAnimation();
            UpdateFlipResult();
        }

        coinCanvas.InvalidateVisual();
    }

    private void UpdateFlipResult()
    {
        if (headsScore >= WinsNeeded)
        {
            seriesOver = true;
            StopAutoFlip();
            resultLabel.Text = $"Heads wins the best of {bestOfNumericUpDown.Value}!";
        }
        else if (tailsScore >= WinsNeeded)
        {
            seriesOver = true;
            StopAutoFlip();
            resultLabel.Text = $"Tails wins the best of {bestOfNumericUpDown.Value}!";
        }
        else
        {
            resultLabel.Text = currentSide == CoinSide.Heads ? "Heads" : "Tails";
        }
    }

    private void UpdateScoreLabel()
    {
        scoreLabel.Text = $"Heads: {headsScore}   Tails: {tailsScore}";
        bestOfIndicator.Text = $"First to {WinsNeeded}";
    }

    private static CoinSide Opposite(CoinSide side) => side == CoinSide.Heads ? CoinSide.Tails : CoinSide.Heads;

    private void PlaySound()
    {
        if (soundCheckBox.IsChecked != true)
        {
            return;
        }

        if (windowsSoundPlayer is not null)
        {
            windowsSoundPlayer.Play();
            return;
        }

        string[] commands = OperatingSystem.IsLinux()
            ? ["pw-play", "paplay", "aplay"]
            : OperatingSystem.IsMacOS() ? ["afplay"] : [];

        foreach (string command in commands)
        {
            try
            {
                Process.Start(new ProcessStartInfo(command, $"\"{soundPath}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return;
            }
            catch (Exception) when (command != commands[^1])
            {
            }
        }
    }

    private static string ExtractSound()
    {
        string path = Path.Combine(Path.GetTempPath(), "coinflip-sound.wav");
        if (!File.Exists(path))
        {
            using Stream? resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("CoinFlip.coin-flip.wav");
            if (resource is null)
            {
                return path;
            }

            using FileStream output = File.Create(path);
            resource.CopyTo(output);
        }

        return path;
    }

    protected override void OnClosed(EventArgs e)
    {
        autoFlipTimer.Stop();
        animationTimer.Stop();
        windowsSoundPlayer?.Dispose();
        base.OnClosed(e);
    }
}

public sealed class CoinCanvas : Control
{
    public static readonly DirectProperty<CoinCanvas, CoinSideState> SideProperty =
        AvaloniaProperty.RegisterDirect<CoinCanvas, CoinSideState>(nameof(Side), o => o.Side);

    public static readonly DirectProperty<CoinCanvas, double> SpinProgressProperty =
        AvaloniaProperty.RegisterDirect<CoinCanvas, double>(nameof(SpinProgress), o => o.SpinProgress);

    private CoinSideState side;
    private double spinProgress;

    public CoinSideState Side
    {
        get => side;
        set => SetAndRaise(SideProperty, ref side, value);
    }

    public double SpinProgress
    {
        get => spinProgress;
        set => SetAndRaise(SpinProgressProperty, ref spinProgress, value);
    }

    public event EventHandler? RenderStateChanged;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, Bounds);

        double diameter = Math.Max(40, Math.Min(Bounds.Width, Bounds.Height) - 24);
        double scale = SpinProgress is > 0 and < 1 ? Math.Abs(Math.Cos(SpinProgress * Math.PI * 4)) : 1;
        double width = Math.Max(8, diameter * scale);
        var rect = new Rect((Bounds.Width - width) / 2, (Bounds.Height - diameter) / 2, width, diameter);
        var center = rect.Center;

        var light = Side == CoinSideState.Heads ? Color.FromRgb(255, 232, 112) : Color.FromRgb(245, 248, 255);
        var dark = Side == CoinSideState.Heads ? Color.FromRgb(184, 128, 12) : Color.FromRgb(115, 126, 145);
        var gradient = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = [new GradientStop(light, 0), new GradientStop(dark, 1)]
        };

        context.DrawEllipse(gradient, new Pen(Brushes.White, 4), center, width / 2, diameter / 2);
        double sheenWidth = Math.Max(4, width / 4);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(105, 255, 255, 255)), null,
            new Point(rect.Left + width / 5 + sheenWidth / 2, rect.Top + diameter / 6 + Math.Max(8, diameter / 10) / 2),
            sheenWidth / 2, Math.Max(8, diameter / 10) / 2);

        double inset = Math.Min(20, Math.Max(2, width / 4));
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(45, 45, 45)), 2),
            new Point(center.X, center.Y), Math.Max(2, (width - inset * 2) / 2), Math.Max(2, (diameter - 40) / 2));

        if (width > diameter / 3)
        {
            var text = new TextLayout(Side == CoinSideState.Heads ? "H" : "T", new Typeface("Arial"), diameter * 0.3, Brushes.Black);
            text.Draw(context, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SideProperty || change.Property == SpinProgressProperty)
        {
            RenderStateChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

public enum CoinSideState
{
    Heads,
    Tails
}
