using BackgroundStudio.Core;
using BackgroundStudio.Services;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BackgroundStudio;

public partial class MainWindow : Window
{
    private readonly ModelManager modelManager = new();
    private readonly FfmpegManager ffmpegManager = new();
    private readonly ObservableCollection<BatchJob> jobs = [];
    private readonly ObservableCollection<BatchJob> results = [];
    private readonly string outputFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        "Background Studio");
    private string? backgroundPath;
    private BatchJob? selectedJob;
    private BitmapSource? previewResult;
    private CancellationTokenSource? cancellation;
    private bool isBusy;
    private bool closeRequested;

    public MainWindow()
    {
        InitializeComponent();
        Directory.CreateDirectory(outputFolder);
        QueueList.ItemsSource = jobs;
        ResultsList.ItemsSource = results;
        OutputFolderText.Text = $"자동 저장 위치\n{outputFolder}";
        RefreshModelStatus();
        RefreshFfmpegStatus();
    }

    private async void DownloadFfmpeg_Click(object sender, RoutedEventArgs e) => await EnsureFfmpegAsync();

    private async Task<bool> EnsureFfmpegAsync()
    {
        if (FfmpegManager.IsAvailable())
        {
            RefreshFfmpegStatus();
            return true;
        }
        try
        {
            SetBusy(true, "FFmpeg 8.1.2를 앱 전용 폴더에 내려받는 중입니다.");
            await ffmpegManager.EnsureAsync(
                new Progress<double>(value => ProgressBar.Value = value),
                CancellationToken.None);
            StatusText.Text = "FFmpeg 준비 완료 · PATH 설정은 필요하지 않습니다.";
            return true;
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
            return false;
        }
        finally
        {
            SetBusy(false);
            RefreshFfmpegStatus();
        }
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

    private void OpenImage_Click(object sender, RoutedEventArgs e) =>
        AddFiles(false, "이미지|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.tif;*.tiff");

    private void OpenVideo_Click(object sender, RoutedEventArgs e) =>
        AddFiles(true, "동영상|*.mp4;*.mov;*.webm;*.mkv;*.avi;*.m4v");

    private void AddFiles(bool isVideo, string filter)
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Multiselect = true
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        foreach (var path in dialog.FileNames)
        {
            if (jobs.Any(job => path.Equals(job.SourcePath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            jobs.Add(new BatchJob(path, isVideo));
        }
        if (jobs.Count > 0)
        {
            QueueList.SelectedItem = jobs[^1];
        }
        RefreshActions();
        StatusText.Text = $"{dialog.FileNames.Length}개 파일을 대기열에 추가했습니다. 설정 후 '대기열 전체 변환'을 누르세요.";
    }

    private void OpenBackground_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "이미지|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        backgroundPath = dialog.FileName;
        BackgroundNameText.Text = Path.GetFileName(backgroundPath);
    }

    private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (QueueList.SelectedItem is BatchJob job)
        {
            SelectJob(job);
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is BatchJob job)
        {
            QueueList.SelectedItem = job;
            SelectJob(job);
        }
    }

    private void SelectJob(BatchJob job)
    {
        selectedJob = job;
        SourceNameText.Text = job.Name;
        if (job.Preview is not null)
        {
            PreviewImage.Source = job.Preview;
            EmptyState.Visibility = Visibility.Collapsed;
            return;
        }
        if (!job.IsVideo)
        {
            try
            {
                job.Preview = ImageComposer.Load(job.SourcePath);
                PreviewImage.Source = job.Preview;
                EmptyState.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                job.Status = $"읽기 오류: {exception.Message}";
            }
        }
        else
        {
            PreviewImage.Source = null;
            EmptyState.Visibility = Visibility.Visible;
        }
    }

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (selectedJob is null || isBusy)
        {
            return;
        }
        results.Remove(selectedJob);
        jobs.Remove(selectedJob);
        selectedJob = null;
        ClearPreview();
        RefreshActions();
        StatusText.Text = "선택한 작업을 대기열에서 삭제했습니다. 원본과 저장 파일은 삭제하지 않았습니다.";
    }

    private void ClearQueue_Click(object sender, RoutedEventArgs e)
    {
        if (isBusy)
        {
            return;
        }
        jobs.Clear();
        results.Clear();
        selectedJob = null;
        ClearPreview();
        RefreshActions();
        StatusText.Text = "대기열과 화면 목록을 초기화했습니다. 이미 저장된 파일은 그대로 남아 있습니다.";
    }

    private void ClearPreview()
    {
        previewResult = null;
        PreviewImage.Source = null;
        SourceNameText.Text = "대기열에서 파일을 선택하세요.";
        EmptyState.Visibility = Visibility.Visible;
    }

    private void Requeue_Click(object sender, RoutedEventArgs e)
    {
        if (selectedJob is null || isBusy)
        {
            return;
        }
        selectedJob.Status = "대기";
        selectedJob.Progress = 0;
        selectedJob.OutputPath = null;
        results.Remove(selectedJob);
        RefreshActions();
    }

    private async void Process_Click(object sender, RoutedEventArgs e)
    {
        var pending = jobs.Where(job => job.Status == "대기" || job.Status.StartsWith("오류", StringComparison.Ordinal)).ToArray();
        if (pending.Length == 0)
        {
            StatusText.Text = "처리할 대기 작업이 없습니다. 완료 작업은 '선택 작업 다시 대기'로 재처리할 수 있습니다.";
            return;
        }
        if (SelectedMode() == BackgroundMode.Image && backgroundPath is null)
        {
            ShowError("배경 이미지를 먼저 선택하세요.");
            return;
        }
        if (pending.Any(job => job.IsVideo) && !VideoProcessor.IsFfmpegAvailable() && !await EnsureFfmpegAsync())
        {
            return;
        }

        cancellation = new CancellationTokenSource();
        SetBusy(true, $"대기열 {pending.Length}개 작업을 순차 처리합니다.");
        try
        {
            using var engine = new U2NetEngine(modelManager.ModelPath);
            var options = CurrentOptions();
            var background = backgroundPath is null ? null : ImageComposer.Load(backgroundPath);
            for (var index = 0; index < pending.Length; index++)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                var job = pending[index];
                QueueList.SelectedItem = job;
                job.Status = $"처리 중 · {index + 1}/{pending.Length}";
                job.Progress = 0;
                StatusText.Text = $"{job.Name} 처리 중 · 전체 {index + 1}/{pending.Length}";
                try
                {
                    if (job.IsVideo)
                    {
                        await ProcessVideoJob(job, engine, options, cancellation.Token);
                    }
                    else
                    {
                        await ProcessImageJob(job, engine, options, background, cancellation.Token);
                    }
                    job.Status = "완료 · 자동 저장됨";
                    job.Progress = 1;
                    if (!results.Contains(job))
                    {
                        results.Insert(0, job);
                    }
                }
                catch (OperationCanceledException)
                {
                    job.Status = "취소됨";
                    throw;
                }
                catch (Exception exception)
                {
                    job.Status = $"오류 · {exception.Message}";
                }
            }
            StatusText.Text = $"대기열 처리가 끝났습니다. 결과 {results.Count}개를 자동 저장했습니다.";
            EditorTabs.SelectedIndex = 4;
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "현재 작업을 취소했습니다. 아직 시작하지 않은 항목은 대기 상태로 남았습니다.";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
        finally
        {
            cancellation?.Dispose();
            cancellation = null;
            SetBusy(false);
            if (closeRequested)
            {
                Close();
            }
        }
    }

    private async Task ProcessImageJob(
        BatchJob job,
        U2NetEngine engine,
        EditOptions options,
        BitmapSource? background,
        CancellationToken token)
    {
        var original = ImageComposer.Load(job.SourcePath);
        var cutout = await Task.Run(
            () => engine.Remove(original, options.MaskThreshold, options.EdgeSoftness),
            token);
        var composed = ImageComposer.Compose(original, cutout, options, background);
        var extension = SelectedTag(ImageFormatCombo);
        var output = UniqueOutputPath(job.SourcePath, extension);
        if (extension == "svg")
        {
            ImageComposer.SaveSvgOutline(
                ImageComposer.PrepareForeground(cutout, options),
                output,
                options.OutlineColor,
                options.OutlineWidth);
        }
        else
        {
            ImageComposer.Save(composed, output);
        }
        job.OutputPath = output;
        job.Preview = composed;
        previewResult = composed;
        PreviewImage.Source = composed;
        EmptyState.Visibility = Visibility.Collapsed;
    }

    private async Task ProcessVideoJob(
        BatchJob job,
        U2NetEngine engine,
        EditOptions options,
        CancellationToken token)
    {
        var extension = SelectedTag(VideoFormatCombo);
        if (IsTransparentOutput(options) && extension is not "webm" and not "mov")
        {
            extension = "webm";
            StatusText.Text = "투명 동영상은 호환되는 WebM으로 자동 저장합니다.";
        }
        var output = UniqueOutputPath(job.SourcePath, extension);
        var processor = new VideoProcessor(engine);
        await processor.ProcessAsync(
            job.SourcePath,
            output,
            options,
            backgroundPath,
            new Progress<double>(value =>
            {
                job.Progress = value;
                ProgressBar.Value = value;
            }),
            token);
        job.OutputPath = output;
    }

    private string UniqueOutputPath(string source, string extension)
    {
        Directory.CreateDirectory(outputFolder);
        var stem = Path.GetFileNameWithoutExtension(source);
        var candidate = Path.Combine(outputFolder, $"{stem}-background.{extension}");
        for (var number = 2; File.Exists(candidate); number++)
        {
            candidate = Path.Combine(outputFolder, $"{stem}-background-{number}.{extension}");
        }
        return candidate;
    }

    private static string SelectedTag(ComboBox combo) =>
        ((ComboBoxItem)combo.SelectedItem).Tag.ToString()!;

    private void OpenOutputFolder_Click(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(outputFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{outputFolder}\"") { UseShellExecute = true });
    }

    private void OpenSelectedResult_Click(object sender, RoutedEventArgs e)
    {
        var job = ResultsList.SelectedItem as BatchJob ?? selectedJob;
        if (job?.OutputPath is null || !File.Exists(job.OutputPath))
        {
            StatusText.Text = "열 수 있는 완료 결과를 먼저 선택하세요.";
            return;
        }
        Process.Start(new ProcessStartInfo(job.OutputPath) { UseShellExecute = true });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => cancellation?.Cancel();

    private void ModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        var mode = SelectedMode();
        ColorPanel.Visibility = mode == BackgroundMode.Color ? Visibility.Visible : Visibility.Collapsed;
        BackgroundPanel.Visibility = mode == BackgroundMode.Image ? Visibility.Visible : Visibility.Collapsed;
        BlurPanel.Visibility = mode == BackgroundMode.Blur ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RenderModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            OutlinePanel.Visibility = SelectedRenderMode() == RenderMode.Outline
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private BackgroundMode SelectedMode() =>
        Enum.Parse<BackgroundMode>(((ComboBoxItem)ModeCombo.SelectedItem).Tag.ToString()!);

    private ForegroundFilter SelectedFilter() =>
        Enum.Parse<ForegroundFilter>(((ComboBoxItem)FilterCombo.SelectedItem).Tag.ToString()!);

    private RenderMode SelectedRenderMode() =>
        Enum.Parse<RenderMode>(((ComboBoxItem)RenderModeCombo.SelectedItem).Tag.ToString()!);

    private EditOptions CurrentOptions() => new(
        SelectedMode(),
        ColorText.Text,
        BlurSlider.Value,
        ShadowBlurSlider.Value,
        ShadowOpacitySlider.Value,
        ShadowXSlider.Value,
        ShadowYSlider.Value,
        ThresholdSlider.Value,
        SoftnessSlider.Value,
        SelectedFilter(),
        SelectedRenderMode(),
        SubjectScaleSlider.Value,
        SubjectXSlider.Value,
        SubjectYSlider.Value,
        AutoCenterCheck.IsChecked == true,
        (int)Math.Round(OutlineWidthSlider.Value),
        OutlineColorText.Text,
        BrightnessSlider.Value,
        ContrastSlider.Value,
        SaturationSlider.Value,
        TemperatureSlider.Value,
        HueSlider.Value,
        ForegroundOpacitySlider.Value,
        RotationSlider.Value,
        FlipHorizontalCheck.IsChecked == true,
        FlipVerticalCheck.IsChecked == true,
        (int)Math.Round(MaskExpansionSlider.Value),
        Enum.Parse<CanvasAspect>(((ComboBoxItem)CanvasAspectCombo.SelectedItem).Tag.ToString()!));

    private static bool IsTransparentOutput(EditOptions options) =>
        (options.Mode == BackgroundMode.Transparent && options.RenderMode != RenderMode.Mask)
        || options.RenderMode == RenderMode.Outline;

    private void RefreshModelStatus()
    {
        var ready = modelManager.IsReady;
        ModelStatusDot.Fill = ready
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(200, 144, 0));
        ModelStatusText.Text = ready ? "모델 준비됨" : "모델 필요";
        DownloadModelButton.Content = ready ? "모델 확인 완료" : "모델 준비";
        RefreshActions();
    }

    private void RefreshFfmpegStatus()
    {
        var ready = FfmpegManager.IsAvailable();
        FfmpegStatusDot.Fill = ready
            ? (Brush)FindResource("AccentBrush")
            : new SolidColorBrush(Color.FromRgb(200, 144, 0));
        FfmpegStatusText.Text = ready ? "FFmpeg 준비됨" : "FFmpeg 필요";
        DownloadFfmpegButton.Content = ready ? "FFmpeg 확인 완료" : "FFmpeg 준비";
    }

    private void RefreshActions()
    {
        if (!IsLoaded)
        {
            return;
        }
        ProcessButton.IsEnabled = !isBusy && modelManager.IsReady && jobs.Any(job => job.Status == "대기" || job.Status.StartsWith("오류", StringComparison.Ordinal));
        RequeueButton.IsEnabled = !isBusy && selectedJob is not null;
        RemoveSelectedButton.IsEnabled = !isBusy && selectedJob is not null;
        ClearQueueButton.IsEnabled = !isBusy && jobs.Count > 0;
    }

    private void SetBusy(bool busy, string? message = null)
    {
        isBusy = busy;
        DownloadModelButton.IsEnabled = !busy;
        DownloadFfmpegButton.IsEnabled = !busy;
        OpenImageButton.IsEnabled = !busy;
        OpenVideoButton.IsEnabled = !busy;
        CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (!busy)
        {
            ProgressBar.Value = 0;
        }
        if (message is not null)
        {
            StatusText.Text = message;
        }
        RefreshActions();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!isBusy || closeRequested)
        {
            return;
        }
        closeRequested = true;
        e.Cancel = true;
        StatusText.Text = "현재 작업을 안전하게 취소한 뒤 프로그램을 닫습니다.";
        cancellation?.Cancel();
    }

    private void ShowError(string message)
    {
        StatusText.Text = message;
        MessageBox.Show(this, message, "Background Studio", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

public sealed class BatchJob(string sourcePath, bool isVideo) : INotifyPropertyChanged
{
    private string status = "대기";
    private double progress;
    private string? outputPath;
    private BitmapSource? preview;

    public string SourcePath { get; } = sourcePath;
    public string Name { get; } = Path.GetFileName(sourcePath);
    public bool IsVideo { get; } = isVideo;

    public string Status
    {
        get => status;
        set => SetField(ref status, value);
    }

    public double Progress
    {
        get => progress;
        set => SetField(ref progress, value);
    }

    public string? OutputPath
    {
        get => outputPath;
        set => SetField(ref outputPath, value);
    }

    public BitmapSource? Preview
    {
        get => preview;
        set => SetField(ref preview, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
