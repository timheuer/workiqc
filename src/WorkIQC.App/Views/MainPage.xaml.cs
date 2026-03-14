using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.Media.Core;
using Windows.Media.Playback;
using WorkIQC.App.Models;
using WorkIQC.App.Services;
using WorkIQC.App.ViewModels;
using Windows.System;
using Windows.UI.Core;

namespace WorkIQC.App.Views
{
    public sealed partial class MainPage : Page
    {
        private bool _isInitialized;
        private MediaPlayer? _notificationPlayer;

        public MainPage(MainPageViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = ViewModel;
            ViewModel.Messages.CollectionChanged += OnMessagesChanged;
            ViewModel.StreamCompleted += OnStreamCompleted;
            AttachMessageHandlers(ViewModel.Messages);
            ActualThemeChanged += OnActualThemeChanged;
        }

        public MainPageViewModel ViewModel { get; }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            await ViewModel.InitializeAsync();
            SyncSidebarSelection();
            ScheduleTranscriptRefresh();

            if (ViewModel.RequiresSetupPrompt)
            {
                await ShowSetupDialogAsync();
            }
        }

        private async void OnNewChatClicked(object sender, RoutedEventArgs e)
        {
            await ViewModel.StartNewChatAsync();
            SyncSidebarSelection();
            ComposerTextBox.Focus(FocusState.Programmatic);
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            ViewModel.ShowSettings();
            ComposerTextBox.Focus(FocusState.Programmatic);
        }

        private void OnBackToConversationClicked(object sender, RoutedEventArgs e)
            => ViewModel.ReturnToConversation();

        private async void OnConversationSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ConversationList.SelectedItem is not ConversationListItemViewModel conversation)
            {
                return;
            }

            await ViewModel.SelectConversationAsync(conversation.Id);
            ScheduleTranscriptRefresh();
        }

        private async void OnSendClicked(object sender, RoutedEventArgs e)
        {
            await SendCurrentPromptAsync();
        }

        private async void OnSuggestionClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string suggestion)
            {
                ViewModel.ComposerText = suggestion;
                await SendCurrentPromptAsync();
            }
        }

        private async void OnComposerPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var shiftState = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift);
            if (!ComposerInputBehavior.ShouldSendOnKeyDown(e.Key, shiftState))
            {
                return;
            }

            e.Handled = true;
            await SendCurrentPromptAsync();
        }

        private async Task SendCurrentPromptAsync()
        {
            await ViewModel.SendAsync();
            SyncSidebarSelection();
            ComposerTextBox.Focus(FocusState.Programmatic);
            ScheduleTranscriptRefresh();
        }

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems is not null)
            {
                foreach (var message in e.OldItems.OfType<ChatMessageViewModel>())
                {
                    message.PropertyChanged -= OnMessagePropertyChanged;
                }
            }

            if (e.NewItems is not null)
            {
                AttachMessageHandlers(e.NewItems.OfType<ChatMessageViewModel>());
            }

            DispatcherQueue.TryEnqueue(() =>
            {
                ScheduleTranscriptRefresh();
            });
        }

        private void ScrollToLatestMessage()
        {
            if (ViewModel.Messages.Count == 0)
            {
                return;
            }

            MessagesList.ScrollIntoView(ViewModel.Messages[ViewModel.Messages.Count - 1]);
        }

        private void SyncSidebarSelection()
        {
            if (ViewModel.SelectedConversationId is null)
            {
                ConversationList.SelectedItem = null;
                return;
            }

            ConversationList.SelectedItem = ViewModel.SidebarItems.FirstOrDefault(item => item.Id == ViewModel.SelectedConversationId);
        }

        private void OnActualThemeChanged(FrameworkElement sender, object args)
            => ViewModel.RefreshTheme();

        private void OnStreamCompleted(object? sender, EventArgs e)
        {
            try
            {
                var soundPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icq-uh-oh.mp3");
                _notificationPlayer?.Dispose();
                _notificationPlayer = new MediaPlayer
                {
                    Source = MediaSource.CreateFromUri(new Uri(soundPath)),
                    AudioCategory = MediaPlayerAudioCategory.Alerts
                };
                _notificationPlayer.Play();
            }
            catch
            {
                // Sound playback is best-effort
            }
        }

        private void OnMessagesListSizeChanged(object sender, SizeChangedEventArgs e)
            => ScheduleTranscriptRefresh();

        private void OnMessagesListContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (args.ItemContainer is not ListViewItem container
                || args.Item is not ChatMessageViewModel message)
            {
                return;
            }

            container.Loaded -= OnMessageContainerLoaded;
            container.Loaded += OnMessageContainerLoaded;
            ApplyBubbleWidth(container, message);
        }

        private async void OnOpenWorkIqTermsClicked(object sender, RoutedEventArgs e)
        {
            if (Uri.TryCreate(ViewModel.SetupEulaUrl, UriKind.Absolute, out var eulaUri))
            {
                await Launcher.LaunchUriAsync(eulaUri);
            }
        }

        private async void OnAcceptWorkIqTermsClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new ProgressRing { Width = 14, Height = 14, IsActive = true },
                        new TextBlock { Text = "Completing consent…", VerticalAlignment = VerticalAlignment.Center }
                    }
                };
            }

            try
            {
                await ViewModel.AcceptWorkIqTermsAsync();
            }
            finally
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                    btn.Content = "Complete consent bootstrap";
                }
            }
        }

        private async void OnLaunchCopilotSignInClicked(object sender, RoutedEventArgs e)
        {
            LaunchCommandInTerminal(ViewModel.SetupAuthenticationCommandText);

            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        new ProgressRing { Width = 14, Height = 14, IsActive = true },
                        new TextBlock { Text = "Recording handoff…", VerticalAlignment = VerticalAlignment.Center }
                    }
                };
            }

            try
            {
                await ViewModel.RecordAuthenticationHandoffAsync(ViewModel.SetupAuthenticationCommandText);
            }
            finally
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                    btn.Content = "Open sign-in bootstrap";
                }
            }
        }

        private async void OnRecheckSetupClicked(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = "Rechecking…";
            }

            try
            {
                await ViewModel.RefreshSetupAsync();
            }
            finally
            {
                if (sender is Button btn)
                {
                    btn.IsEnabled = true;
                    btn.Content = "Recheck bootstrap";
                }
            }
        }

        private async void OnDeleteConversationClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ConversationListItemViewModel conversation })
            {
                return;
            }

            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Delete thread?",
                PrimaryButtonText = "Delete",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                Content = new TextBlock
                {
                    Text = $"\"{conversation.Title}\" and its saved transcript will be removed from local history.",
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 420
                }
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await ViewModel.DeleteConversationAsync(conversation.Id);
            SyncSidebarSelection();
        }

        private void OnCopyMessageClicked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement { Tag: ChatMessageViewModel message })
            {
                return;
            }

            var content = message.Content;

            // Strip follow-up suggestions below the last horizontal rule (---, ***, ___)
            var lastHrIndex = FindLastHorizontalRule(content);
            if (lastHrIndex >= 0)
            {
                content = content[..lastHrIndex].TrimEnd();
            }

            var dataPackage = new Windows.ApplicationModel.DataTransfer.DataPackage();
            dataPackage.SetText(content);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(dataPackage);
        }

        private static int FindLastHorizontalRule(string text)
        {
            var lines = text.Split('\n');
            for (var i = lines.Length - 1; i >= 0; i--)
            {
                var trimmed = lines[i].Trim();
                if (trimmed.Length >= 3
                    && (trimmed.Replace("-", "") == ""
                        || trimmed.Replace("*", "") == ""
                        || trimmed.Replace("_", "") == ""))
                {
                    return text.IndexOf(lines[i], StringComparison.Ordinal);
                }
            }

            return -1;
        }

        private async Task ShowSetupDialogAsync()
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Complete WorkICQ bootstrap",
                CloseButtonText = "Continue in preview",
                DefaultButton = ContentDialogButton.Close,
                Content = new StackPanel
                {
                    Spacing = 12,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Before the first live WorkICQ session, complete WorkICQ consent and launch Copilot sign-in from the app-owned bootstrap card in Settings.",
                            TextWrapping = TextWrapping.Wrap,
                            MaxWidth = 520
                        },
                        new TextBlock
                        {
                            Text = $"MCP package: {ViewModel.SetupPackageReferenceText}",
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = $"Workspace: {ViewModel.SetupWorkspaceText}",
                            TextWrapping = TextWrapping.Wrap
                        }
                    }
                }
            };

            await dialog.ShowAsync();
        }

        private static void LaunchCommandInTerminal(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
            {
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k {commandLine}",
                UseShellExecute = true
            });
        }

        private void AttachMessageHandlers(IEnumerable<ChatMessageViewModel> messages)
        {
            foreach (var message in messages)
            {
                message.PropertyChanged -= OnMessagePropertyChanged;
                message.PropertyChanged += OnMessagePropertyChanged;
            }
        }

        private void OnMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ChatMessageViewModel)
            {
                return;
            }

            if (e.PropertyName is nameof(ChatMessageViewModel.Content)
                or nameof(ChatMessageViewModel.IsStreaming))
            {
                ScheduleTranscriptRefresh();
            }
        }

        private void OnMessageContainerLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ListViewItem { DataContext: ChatMessageViewModel message } container)
            {
                return;
            }

            ApplyBubbleWidth(container, message);
        }

        private void ScheduleTranscriptRefresh()
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                MessagesList.UpdateLayout();
                ScrollToLatestMessage();
                RefreshMessageBubbleWidths();
            });
        }

        private void RefreshMessageBubbleWidths()
        {
            if (MessagesList.ActualWidth <= 0)
            {
                return;
            }

            foreach (var message in ViewModel.Messages)
            {
                if (MessagesList.ContainerFromItem(message) is ListViewItem container)
                {
                    ApplyBubbleWidth(container, message);
                }
            }
        }

        private void ApplyBubbleWidth(ListViewItem container, ChatMessageViewModel message)
        {
            if (container.ContentTemplateRoot is not FrameworkElement templateRoot
                || templateRoot.FindName("MessageBubbleBorder") is not Border bubbleBorder)
            {
                return;
            }

            var transcriptWidth = Math.Max(0d, MessagesList.ActualWidth - 24d);
            if (transcriptWidth <= 0)
            {
                return;
            }

            var minWidth = Math.Clamp(transcriptWidth * 0.46d, 340d, 540d);
            var maxWidth = Math.Clamp(transcriptWidth * 0.88d, 520d, 980d);
            var preferredWidth = ComputeBubbleWidth(message, minWidth, maxWidth);
            var bubbleContentMinWidth = Math.Max(1d, minWidth - 36d);
            var bubbleContentMaxWidth = Math.Max(1d, maxWidth - 36d);
            var bubbleContentWidth = Math.Max(1d, preferredWidth - 36d);

            bubbleBorder.MinWidth = minWidth;
            bubbleBorder.MaxWidth = maxWidth;
            bubbleBorder.Width = preferredWidth;

            if (templateRoot.FindName("MarkdownContentView") is FrameworkElement markdownContentView)
            {
                markdownContentView.MinWidth = bubbleContentMinWidth;
                markdownContentView.MaxWidth = bubbleContentMaxWidth;
                markdownContentView.Width = bubbleContentWidth;
            }
        }

        private static double ComputeBubbleWidth(ChatMessageViewModel message, double minWidth, double maxWidth)
        {
            var content = message.Content ?? string.Empty;
            var lineCount = content.Split('\n').Length;
            var hasStructuredMarkdown =
                content.Contains("```", StringComparison.Ordinal)
                || content.Contains("|", StringComparison.Ordinal)
                || content.Contains("##", StringComparison.Ordinal)
                || content.Contains("- ", StringComparison.Ordinal)
                || content.Contains("* ", StringComparison.Ordinal)
                || lineCount > 1;

            if (message.IsStreaming || hasStructuredMarkdown || content.Length >= 140)
            {
                return maxWidth;
            }

            var widthRange = Math.Max(0d, maxWidth - minWidth);
            var normalizedLength = Math.Clamp(content.Length / 140d, 0d, 1d);
            var easedLength = Math.Sqrt(normalizedLength);
            var preferredWidth = minWidth + (widthRange * easedLength);

            if (message.Role == ChatRole.User)
            {
                preferredWidth = Math.Min(preferredWidth, Math.Clamp(maxWidth * 0.88d, minWidth, maxWidth));
            }

            return Math.Clamp(preferredWidth, minWidth, maxWidth);
        }
    }
}
