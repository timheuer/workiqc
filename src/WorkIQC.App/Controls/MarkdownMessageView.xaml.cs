using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using WorkIQC.App.Services;
using Windows.System;

namespace WorkIQC.App.Controls;

public sealed partial class MarkdownMessageView : UserControl
{
    public static readonly DependencyProperty MarkdownProperty = DependencyProperty.Register(
        nameof(Markdown),
        typeof(string),
        typeof(MarkdownMessageView),
        new PropertyMetadata(string.Empty, OnRenderPropertyChanged));

    public static readonly DependencyProperty ForegroundBrushProperty = DependencyProperty.Register(
        nameof(ForegroundBrush),
        typeof(Brush),
        typeof(MarkdownMessageView),
        new PropertyMetadata(null, OnRenderPropertyChanged));

    public static readonly DependencyProperty BackgroundBrushProperty = DependencyProperty.Register(
        nameof(BackgroundBrush),
        typeof(Brush),
        typeof(MarkdownMessageView),
        new PropertyMetadata(null, OnRenderPropertyChanged));

    private bool _isLoaded;
    private bool _isWebViewReady;
    private bool _renderInProgress;
    private bool _renderPending;
    private string? _lastRenderedHtml;
    private Task? _initializeTask;
    private Exception? _initializationFailure;

    public MarkdownMessageView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
        ActualThemeChanged += OnActualThemeChanged;
    }

    public string Markdown
    {
        get => (string)GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    public Brush? ForegroundBrush
    {
        get => (Brush?)GetValue(ForegroundBrushProperty);
        set => SetValue(ForegroundBrushProperty, value);
    }

    public Brush? BackgroundBrush
    {
        get => (Brush?)GetValue(BackgroundBrushProperty);
        set => SetValue(BackgroundBrushProperty, value);
    }

    private static void OnRenderPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not MarkdownMessageView view)
        {
            return;
        }

        view.UpdateFallbackState();
        if (view._isLoaded)
        {
            _ = view.RenderAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;
        ApplyAvailableWidth(ActualWidth);
        UpdateFallbackState();
        await RenderAsync();
    }

    private async void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width <= 0)
        {
            return;
        }

        ApplyAvailableWidth(e.NewSize.Width);
        if (!_isWebViewReady)
        {
            return;
        }

        await UpdateHeightAsync();
    }

    private async void OnActualThemeChanged(FrameworkElement sender, object args)
        => await RenderAsync();

    private async Task RenderAsync()
    {
        if (!_isLoaded)
        {
            return;
        }

        if (_renderInProgress)
        {
            _renderPending = true;
            return;
        }

        _renderInProgress = true;
        try
        {
            do
            {
                _renderPending = false;
                await EnsureWebViewAsync();
                if (_initializationFailure is not null || !_isWebViewReady)
                {
                    ShowFallback();
                    continue;
                }

                var html = MarkdownHtmlRenderer.RenderDocument(Markdown ?? string.Empty, BuildPalette());
                if (string.Equals(html, _lastRenderedHtml, StringComparison.Ordinal))
                {
                    await UpdateHeightAsync();
                    continue;
                }

                _lastRenderedHtml = html;
                ApplyAvailableWidth(ActualWidth);
                ShowWebView();
                MarkdownWebView.NavigateToString(html);
            }
            while (_renderPending);
        }
        finally
        {
            _renderInProgress = false;
        }
    }

    private Task EnsureWebViewAsync()
        => _initializeTask ??= EnsureWebViewCoreAsync();

    private async Task EnsureWebViewCoreAsync()
    {
        try
        {
            await MarkdownWebView.EnsureCoreWebView2Async();
            MarkdownWebView.NavigationCompleted += OnNavigationCompleted;

            var settings = MarkdownWebView.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;

            MarkdownWebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            MarkdownWebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            _isWebViewReady = true;
        }
        catch (Exception exception)
        {
            _initializationFailure = exception;
        }
    }

    private async void OnNavigationCompleted(WebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess)
        {
            ShowFallback();
            return;
        }

        await UpdateHeightAsync();
    }

    private async Task UpdateHeightAsync()
    {
        try
        {
            var rawHeight = await MarkdownWebView.ExecuteScriptAsync("document.getElementById('content').offsetHeight");
            var height = ParseScriptHeight(rawHeight);
            MarkdownWebView.Height = Math.Max(1, Math.Ceiling(height) + 2);
        }
        catch
        {
            ShowFallback();
        }
    }

    private void ApplyAvailableWidth(double availableWidth)
    {
        if (availableWidth <= 0)
        {
            return;
        }

        var width = Math.Max(1d, Math.Floor(availableWidth));
        Width = width;
        MarkdownWebView.Width = width;
        FallbackTextBlock.MaxWidth = width;
    }

    private async void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(args.Uri)
            || args.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase)
            || args.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        args.Cancel = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private async void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var uri))
        {
            await Launcher.LaunchUriAsync(uri);
        }
    }

    private MarkdownPalette BuildPalette()
    {
        var backgroundColor = ToCssColor(BackgroundBrush) ?? ResolveThemeColor("SurfaceElevatedBackgroundBrush");
        var foregroundColor = ToCssColor(ForegroundBrush) ?? ResolveThemeColor("TextPrimaryBrush");
        var mutedColor = ResolveThemeColor("TextSecondaryBrush");
        var accentColor = ResolveThemeColor("AccentBrush");
        var surfaceSoftColor = ResolveThemeColor("SurfaceSecondaryBackgroundBrush");
        var borderColor = ResolveThemeColor("SurfaceBorderBrush");

        return new MarkdownPalette(
            backgroundColor,
            foregroundColor,
            mutedColor,
            accentColor,
            surfaceSoftColor,
            borderColor);
    }

    private void UpdateFallbackState()
    {
        FallbackTextBlock.Text = Markdown ?? string.Empty;
        FallbackTextBlock.Foreground = ForegroundBrush ?? ThemeBrushResolver.GetBrush("TextPrimaryBrush");
    }

    private void ShowFallback()
    {
        UpdateFallbackState();
        MarkdownWebView.Visibility = Visibility.Collapsed;
        FallbackTextBlock.Visibility = Visibility.Visible;
    }

    private void ShowWebView()
    {
        MarkdownWebView.Visibility = Visibility.Visible;
        FallbackTextBlock.Visibility = Visibility.Collapsed;
    }

    private static string ResolveThemeColor(string key)
        => ToCssColor(ThemeBrushResolver.GetBrush(key));

    private static string? ToCssColor(Brush? brush)
        => brush is SolidColorBrush solidBrush ? ToCssColor(solidBrush) : null;

    private static string ToCssColor(SolidColorBrush brush)
        => $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";

    private static double ParseScriptHeight(string rawHeight)
    {
        if (string.IsNullOrWhiteSpace(rawHeight))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(rawHeight);
            var root = document.RootElement;
            return root.ValueKind switch
            {
                JsonValueKind.Number => root.GetDouble(),
                JsonValueKind.String when double.TryParse(root.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var value) => value,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }
}
