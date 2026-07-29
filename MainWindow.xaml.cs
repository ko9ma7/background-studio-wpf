using BackgroundStudio.Core;
using BackgroundStudio.Services;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BackgroundStudio;

public partial class MainWindow : Window
{
    private readonly ModelManager modelManager = new();
    private string? sourcePath;
    private string? backgroundPath;
    private bool isVideo;
    private BitmapSource? original;
    private BitmapSource? result;
    private CancellationTokenSource? cancellation;

    public MainWindow()
    {
        InitializeComponent();
        RefreshModelStatus();
    }

    private async void DownloadModel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true, "U2NetP 모델을 내려받는 중입니다.");
            await modelManager.EnsureModelAsync(
                new Progress<double>(value => ProgressBar.Value = value),
                CancellationToken.None);
            StatusText.Text = "모델 준비가 끝났습니다.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
            RefreshModelStatus();
        }
    }

    private void OpenImage_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "이미지|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        sourcePath = dialog.FileName;
        isVideo = false;
        original = ImageComposer.Load(sourcePath);
        PreviewImage.Source = original;
        SourceNameText.Text = Path.GetFileName(sourcePath);
        EmptyState.Visibility = Visibility.Collapsed;
        ProcessButton.IsEnabled = modelManager.IsReady;
        SaveButton.IsEnabled = false;
        StatusText.Text = "이미지를 불러왔습니다.";
    }

    private void OpenVideo_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "동영상|*.mp4;*.mov;*.webm;*.mkv;*.avi;*.m4v"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        sourcePath = dialog.FileName;
        isVideo = true;
        original = null;
        result = null;
        PreviewImage.Source = null;
        SourceNameText.Text = Path.GetFileName(sourcePath);
        EmptyState.Visibility = Visibility.Visible;
        ProcessButton.IsEnabled = modelManager.IsReady;
        SaveButton.IsEnabled = false;
        StatusText.Text = VideoProcessor.IsFfmpegAvailable()
            ? "동영상을 불러왔습니다."
            : "FFmpeg가 없어 동영상 처리를 시작할 수 없습니다.";
    }

    private void OpenBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "이미지|*.png;*.jpg;*.jpeg;*.webp;*.bmp"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        backgroundPath = dialog.FileName;
        BackgroundNameText.Text = Path.GetFileName(backgroundPath);
    }

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        var mode = SelectedMode();
        ColorPanel.Visibility = mode == BackgroundMode.Color
            ? Visibility.Visible
            : Visibility.Collapsed;
        BackgroundPanel.Visibility = mode == BackgroundMode.Image
            ? Visibility.Visible
            : Visibility.Collapsed;
        BlurPanel.Visibility = mode == BackgroundMode.Blur
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        if (sourcePath is null)
        {
            return;
        }
        if (SelectedMode() == BackgroundMode.Image && backgroundPath is null)
        {
            ShowError("배경 이미지를 먼저 선택하세요.");
            return;
        }

        cancellation = new CancellationTokenSource();
        try
        {
            SetBusy(true, "배경을 분리하고 있습니다.");
            using var engine = new U2NetEngine(modelManager.ModelPath);
            var options = CurrentOptions();
            if (isVideo)
            {
                var save = new SaveFileDialog
                {
                    Filter = options.Mode == BackgroundMode.Transparent
                        ? "투명 WebM|*.webm"
                        : "MP4 동영상|*.mp4",
                    DefaultExt = options.Mode == BackgroundMode.Transparent ? ".webm" : ".mp4"
                };
                if (save.ShowDialog() != true)
                {
                    return;
                }
                var processor = new VideoProcessor(engine);
                await processor.ProcessAsync(
                    sourcePath,
                    save.FileName,
                    options,
                    backgroundPath,
                    new Progress<double>(value => ProgressBar.Value = value),
                    cancellation.Token);
                StatusText.Text = $"동영상 저장 완료: {save.FileName}";
            }
            else if (original is not null)
            {
                var cutout = await Task.Run(
                    () => engine.Remove(original, options.MaskThreshold, options.EdgeSoftness),
                    cancellation.Token);
                var background = backgroundPath is null
                    ? null
                    : ImageComposer.Load(backgroundPath);
                result = ImageComposer.Compose(original, cutout, options, background);
                PreviewImage.Source = result;
                SaveButton.IsEnabled = true;
                StatusText.Text = "배경 제거와 편집이 끝났습니다.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "작업을 취소했습니다.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            SetBusy(false);
            cancellation.Dispose();
            cancellation = null;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (result is null)
        {
            return;
        }
        var dialog = new SaveFileDialog
        {
            Filter = "PNG 이미지|*.png",
            DefaultExt = ".png",
            FileName = "background-studio-result.png"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        ImageComposer.SavePng(result, dialog.FileName);
        StatusText.Text = $"PNG 저장 완료: {dialog.FileName}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        cancellation?.Cancel();
    }

    private BackgroundMode SelectedMode()
    {
        var item = (ComboBoxItem)ModeCombo.SelectedItem;
        return Enum.Parse<BackgroundMode>(item.Tag.ToString()!);
    }

    private EditOptions CurrentOptions()
    {
        return new EditOptions(
            SelectedMode(),
            ColorText.Text,
            BlurSlider.Value,
            ShadowBlurSlider.Value,
            ShadowOpacitySlider.Value,
            0,
            ShadowYSlider.Value,
            ThresholdSlider.Value,
            SoftnessSlider.Value);
    }

    private void RefreshModelStatus()
    {
        var ready = modelManager.IsReady;
        ModelStatusDot.Fill = ready
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(200, 144, 0));
        ModelStatusText.Text = ready ? "모델 준비됨" : "모델 필요";
        DownloadModelButton.Content = ready ? "모델 확인 완료" : "모델 준비";
        ProcessButton.IsEnabled = ready && sourcePath is not null;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        DownloadModelButton.IsEnabled = !busy;
        ProcessButton.IsEnabled = !busy && modelManager.IsReady && sourcePath is not null;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            ProgressBar.Value = 0;
        }
        if (message is not null)
        {
            StatusText.Text = message;
        }
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "Background Studio", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
