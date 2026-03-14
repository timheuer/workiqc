using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using WorkIQC.App.Models;
using WorkIQC.App.Services;

namespace WorkIQC.App.ViewModels
{
    public sealed class MainPageViewModel : ObservableObject
    {
        private const string RuntimeConnectionBadge = "WorkICQ runtime";
        private const string WorkIqBlockedConnectionBadge = "WorkICQ blocked";
        private readonly Dictionary<string, ConversationSeed> _conversations = new Dictionary<string, ConversationSeed>();
        private readonly IChatShellService _chatShellService;

        public event EventHandler? StreamCompleted;

        private bool _initialized;
        private bool _isSettingsViewActive;
        private bool _isNotificationSoundEnabled = true;
        private string? _selectedConversationId;
        private ShellSetupState _setupState = new(
            RequiresUserAction: true,
            CanAttemptRuntime: false,
            IsEulaAccepted: false,
            IsAuthenticationHandoffStarted: false,
            SummaryText: "WorkICQ setup has not been checked yet.",
            WorkIQPackageReference: WorkIQC.Runtime.Abstractions.Models.WorkIQRuntimeDefaults.PackageReference,
            WorkspacePath: string.Empty,
            McpConfigPath: string.Empty,
            EulaUrl: WorkIQC.Runtime.Abstractions.Models.WorkIQRuntimeDefaults.EulaUrl,
            EulaMarkerPath: string.Empty,
            AuthenticationMarkerPath: string.Empty,
            AuthenticationCommandLine: WorkIQC.Runtime.Abstractions.Models.WorkIQRuntimeDefaults.CopilotLoginCommand,
            Blockers: Array.Empty<SetupCheckItem>(),
            Prerequisites: Array.Empty<SetupCheckItem>());
        private string _composerText = string.Empty;
        private string _connectionBadgeText = "Preview data";
        private string _sidebarFooterText = "Local history is ready. Runtime updates appear here when live handoff is active.";

        public MainPageViewModel(IChatShellService chatShellService)
        {
            _chatShellService = chatShellService;

            ConversationGroups = new ObservableCollection<ConversationGroupViewModel>();
            SidebarItems = new ObservableCollection<ConversationListItemViewModel>();
            Messages = new ObservableCollection<ChatMessageViewModel>();
            SetupBlockers = new ObservableCollection<SetupCheckItem>();
            SetupPrerequisites = new ObservableCollection<SetupCheckItem>();

            Messages.CollectionChanged += (_, _) =>
            {
                RaisePropertyChanged(nameof(TranscriptVisibility));
                RaisePropertyChanged(nameof(EmptyStateVisibility));
                RaisePropertyChanged(nameof(EmptyStateTitle));
                RaisePropertyChanged(nameof(EmptyStateDescription));
            };
        }

        public ObservableCollection<ConversationGroupViewModel> ConversationGroups { get; }

        public ObservableCollection<ConversationListItemViewModel> SidebarItems { get; }

        public ObservableCollection<ChatMessageViewModel> Messages { get; }

        public ObservableCollection<SetupCheckItem> SetupBlockers { get; }

        public ObservableCollection<SetupCheckItem> SetupPrerequisites { get; }

        public string? SelectedConversationId => _selectedConversationId;

        public bool IsSettingsViewActive
        {
            get => _isSettingsViewActive;
            private set
            {
                if (!SetProperty(ref _isSettingsViewActive, value))
                {
                    return;
                }

                RaisePropertyChanged(nameof(SettingsSurfaceVisibility));
                RaisePropertyChanged(nameof(ConversationSurfaceVisibility));
                RaisePropertyChanged(nameof(SettingsButtonLabel));
                RaisePropertyChanged(nameof(SettingsButtonGlyph));
            }
        }

        public string SettingsButtonLabel => "Settings";

        public string SettingsButtonGlyph => "\uE713";

        public bool IsNotificationSoundEnabled
        {
            get => _isNotificationSoundEnabled;
            set => SetProperty(ref _isNotificationSoundEnabled, value);
        }

        public string ComposerText
        {
            get => _composerText;
            set
            {
                if (!SetProperty(ref _composerText, value))
                {
                    return;
                }

                RaisePropertyChanged(nameof(CanSend));
            }
        }

        public bool CanSend => !IsSelectedConversationBusy && !string.IsNullOrWhiteSpace(ComposerText);

        public string SendButtonText => IsSelectedConversationBusy ? "Busy" : "Send";

        public string ComposerHintText => "Press Enter to send. Shift+Enter adds a new line.";

        public string ComposerStatusText
        {
            get
            {
                if (!TryGetSelectedConversation(out var conversation) || !conversation.IsProcessing)
                {
                    return "Ready to send";
                }

                if (conversation.IsSendingPhase)
                {
                    return "Sending prompt…";
                }

                if (conversation.RuntimeActivityText is not null)
                {
                    return conversation.RuntimeActivityText;
                }

                return ResolveStreamingStatusText();
            }
        }

        public string SidebarFooterText
        {
            get => _sidebarFooterText;
            private set => SetProperty(ref _sidebarFooterText, value);
        }

        public string ConnectionBadgeText
        {
            get => _connectionBadgeText;
            private set
            {
                if (!SetProperty(ref _connectionBadgeText, value))
                {
                    return;
                }

                RaisePropertyChanged(nameof(ConnectionBadgeVisibility));
            }
        }

        public Visibility ConnectionBadgeVisibility => string.IsNullOrWhiteSpace(ConnectionBadgeText) ? Visibility.Collapsed : Visibility.Visible;

        public Visibility SetupCardVisibility => _setupState.RequiresUserAction ? Visibility.Visible : Visibility.Collapsed;

        public Visibility SettingsSurfaceVisibility => IsSettingsViewActive ? Visibility.Visible : Visibility.Collapsed;

        public Visibility ConversationSurfaceVisibility => IsSettingsViewActive ? Visibility.Collapsed : Visibility.Visible;

        public bool RequiresSetupPrompt => _setupState.RequiresUserAction;

        public string SetupTitle => _setupState.RequiresUserAction ? "Complete WorkICQ bootstrap" : "WorkICQ bootstrap is ready";

        public string SetupSummaryText => _setupState.SummaryText;

        public string SetupPackageReferenceText => _setupState.WorkIQPackageReference;

        public string SetupWorkspaceText => _setupState.WorkspacePath;

        public string SetupMcpConfigText => _setupState.McpConfigPath;

        public string SetupEulaUrl => _setupState.EulaUrl;

        public string SetupAuthenticationCommandText => _setupState.AuthenticationCommandLine;

        public string EulaStepStatusText => _setupState.IsEulaAccepted ? "Accepted" : "Required";

        public string AuthStepStatusText => _setupState.IsAuthenticationHandoffStarted ? "Completed" : "Required";

        public string EulaStepDescription => _setupState.IsEulaAccepted
            ? $"Verified during WorkICQ bootstrap and recorded at {_setupState.EulaMarkerPath}."
            : $"Review the terms, then complete the WorkICQ consent bootstrap before the first live session. App-local files alone do not complete consent. Evidence is stored at {_setupState.EulaMarkerPath}.";

        public string AuthStepDescription => _setupState.IsAuthenticationHandoffStarted
            ? $"Copilot auth handoff was recorded locally. This does not prove WorkICQ resolved your identity yet. Evidence is tracked at {_setupState.AuthenticationMarkerPath}."
            : $"Run {SetupAuthenticationCommandText} once during bootstrap before the first live WorkICQ session.";

        public Visibility ActivityBadgeVisibility => IsSelectedConversationBusy ? Visibility.Visible : Visibility.Collapsed;

        public bool IsBusyIndicatorActive => IsSelectedConversationBusy;

        public string ActivityBadgeText
        {
            get
            {
                if (!TryGetSelectedConversation(out var conversation) || !conversation.IsProcessing)
                {
                    return string.Empty;
                }

                if (conversation.IsSendingPhase)
                {
                    return "Sending";
                }

                return string.IsNullOrWhiteSpace(conversation.RuntimeActivityText) ? "Streaming" : "Tool activity";
            }
        }

        public Visibility TranscriptVisibility => Messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EmptyStateVisibility => Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string EmptyStateTitle => _selectedConversationId is null ? "Start a calm new conversation" : "This thread is ready for its first prompt";

        public string EmptyStateDescription
            => _selectedConversationId is null
                ? "Choose a recent thread or begin a new one. The shell already remembers local state and keeps the main workspace centered on your next question."
                : "The draft is selected. Send a first prompt whenever you're ready.";

        private bool IsSelectedConversationBusy
            => TryGetSelectedConversation(out var conversation) && conversation.IsProcessing;

        public async Task InitializeAsync()
        {
            if (_initialized)
            {
                return;
            }

            var shellState = await _chatShellService.LoadShellAsync();
            ApplyBootstrapState(shellState);
            _initialized = true;
        }

        public async Task StartNewChatAsync()
        {
            if (_selectedConversationId is not null
                && _conversations.TryGetValue(_selectedConversationId, out var current)
                && current.IsDraft
                && current.Messages.Count == 0)
            {
                SelectConversation(current.Id);
                return;
            }

            var draft = await _chatShellService.CreateConversationAsync();
            UpsertConversation(CreateSeed(draft));
            IsSettingsViewActive = false;
            SelectConversation(draft.Id);
        }

        public Task SelectConversationAsync(string id)
        {
            IsSettingsViewActive = false;
            SelectConversation(id);
            return Task.CompletedTask;
        }

        public void ShowSettings()
            => IsSettingsViewActive = true;

        public void ReturnToConversation()
            => IsSettingsViewActive = false;

        public async Task DeleteConversationAsync(string id)
        {
            if (!_conversations.Remove(id, out var removedConversation))
            {
                return;
            }

            removedConversation.StreamingCts?.Cancel();

            if (removedConversation.IsPersisted)
            {
                await _chatShellService.DeleteConversationAsync(id);
            }

            if (string.Equals(_selectedConversationId, id, StringComparison.Ordinal))
            {
                var nextConversation = _conversations.Values
                    .OrderByDescending(conversation => conversation.UpdatedAt)
                    .FirstOrDefault();

                if (nextConversation is null)
                {
                    ClearSelection();
                }
                else
                {
                    SelectConversation(nextConversation.Id);
                }
            }
            else
            {
                RefreshConversationGroups();
            }
        }

        public async Task SendAsync()
        {
            if (!CanSend)
            {
                return;
            }

            if (_selectedConversationId is null || !_conversations.ContainsKey(_selectedConversationId))
            {
                await StartNewChatAsync();
            }

            if (_selectedConversationId is null || !_conversations.TryGetValue(_selectedConversationId, out var conversation))
            {
                return;
            }

            var prompt = ComposerText.Trim();
            ComposerText = string.Empty;

            var now = DateTime.Now;
            var userMessage = new ChatMessageViewModel(ChatRole.User, "You", prompt, now);
            AddMessageToConversation(conversation, userMessage);
            conversation.Messages.Add(new MessageSeed(ChatRole.User, "You", prompt, now));

            if (conversation.IsDraft)
            {
                conversation.Title = BuildTitle(prompt);
                conversation.IsDraft = false;
            }

            conversation.Preview = prompt;
            conversation.UpdatedAt = now;
            conversation.IsSendingPhase = true;
            SetConversationProcessing(conversation, true);
            SelectConversation(conversation.Id, keepMessages: true);

            // Fire-and-forget: stream the response in the background so the UI stays unblocked
            _ = StreamConversationResponseAsync(conversation, prompt);
        }

        private async Task StreamConversationResponseAsync(ConversationSeed conversation, string prompt)
        {
            try
            {
                await Task.Delay(260);

                var assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, "WorkICQ", string.Empty, DateTime.Now, isStreaming: true);
                AddMessageToConversation(conversation, assistantMessage);

                var cts = new CancellationTokenSource();
                conversation.StreamingCts = cts;

                ShellSendResponse response;
                try
                {
                    response = await _chatShellService.SendAsync(
                        new ShellSendRequest(conversation.Id, conversation.Title, prompt, conversation.SessionId));
                }
                catch
                {
                    conversation.IsSendingPhase = false;
                    assistantMessage.AppendContent("An error occurred while sending the message.");
                    assistantMessage.CompleteStreaming(DateTime.Now);
                    SetConversationProcessing(conversation, false);
                    return;
                }

                PromoteConversationIdentity(conversation, response.ConversationId);
                conversation.Title = response.ConversationTitle;
                conversation.IsPersisted = response.IsPersisted;
                conversation.IsDraft = response.IsDraft;
                conversation.SessionId = response.SessionId;
                conversation.IsSendingPhase = false;

                if (IsConversationSelected(conversation))
                {
                    ConnectionBadgeText = response.ConnectionBadgeText;
                    SidebarFooterText = response.SidebarFooterText;
                }

                conversation.RuntimeActivityText = null;
                RaiseBusyStateChanged();

                var activityTask = ConsumeActivityUpdatesAsync(conversation, response.ActivityStream, response.SidebarFooterText);
                await foreach (var chunk in response.ResponseStream)
                {
                    if (cts.IsCancellationRequested)
                    {
                        break;
                    }

                    assistantMessage.AppendContent(chunk);
                }
                await activityTask;

                var completedAt = DateTime.Now;
                assistantMessage.CompleteStreaming(completedAt);
                conversation.Messages.Add(new MessageSeed(ChatRole.Assistant, "WorkICQ", assistantMessage.Content, completedAt));
                conversation.Preview = assistantMessage.Content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                    ?? assistantMessage.Content;
                conversation.UpdatedAt = completedAt;

                if (IsConversationSelected(conversation))
                {
                    SidebarFooterText = response.SidebarFooterText;
                }

                if (IsNotificationSoundEnabled)
                {
                    StreamCompleted?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                SetConversationProcessing(conversation, false);
                SelectConversation(conversation.Id, keepMessages: true);
            }
        }

        public async Task AcceptWorkIqTermsAsync()
            => ApplySetupState(await _chatShellService.AcceptWorkIqTermsAsync());

        public async Task RecordAuthenticationHandoffAsync(string? loginCommand = null)
            => ApplySetupState(await _chatShellService.RecordAuthenticationHandoffAsync(loginCommand));

        public async Task RefreshSetupAsync()
            => ApplySetupState(await _chatShellService.RefreshSetupAsync());

        private async Task ConsumeActivityUpdatesAsync(ConversationSeed conversation, IAsyncEnumerable<string> activityStream, string baselineSidebarFooter)
        {
            await foreach (var update in activityStream)
            {
                if (string.IsNullOrWhiteSpace(update))
                {
                    conversation.RuntimeActivityText = null;
                    if (IsConversationSelected(conversation))
                    {
                        SidebarFooterText = baselineSidebarFooter;
                    }
                }
                else
                {
                    conversation.RuntimeActivityText = update.Trim();
                    if (IsConversationSelected(conversation))
                    {
                        SidebarFooterText = $"{baselineSidebarFooter} {conversation.RuntimeActivityText}";
                    }
                }

                RaisePropertyChanged(nameof(ComposerStatusText));
                RaisePropertyChanged(nameof(ActivityBadgeText));
            }
        }

        public void RefreshTheme()
        {
            foreach (var item in SidebarItems)
            {
                item.RefreshTheme();
            }

            foreach (var message in Messages)
            {
                message.RefreshTheme();
            }
        }

        private void ApplyBootstrapState(ShellBootstrapState shellState)
        {
            _conversations.Clear();
            foreach (var conversation in shellState.Conversations)
            {
                var seed = CreateSeed(conversation);
                _conversations[seed.Id] = seed;
            }

            ConnectionBadgeText = shellState.ConnectionBadgeText;
            SidebarFooterText = shellState.SidebarFooterText;
            ApplySetupState(shellState.SetupState);

            RefreshConversationGroups();
            var firstConversation = _conversations.Values.OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
            if (firstConversation is not null)
            {
                SelectConversation(firstConversation.Id);
            }
        }

        private void PromoteConversationIdentity(ConversationSeed conversation, string resolvedConversationId)
        {
            if (string.Equals(conversation.Id, resolvedConversationId, StringComparison.Ordinal))
            {
                return;
            }

            _conversations.Remove(conversation.Id);
            conversation.Id = resolvedConversationId;
            _conversations[conversation.Id] = conversation;
        }

        private void UpsertConversation(ConversationSeed conversation)
            => _conversations[conversation.Id] = conversation;

        private static ConversationSeed CreateSeed(ShellConversationSnapshot snapshot)
        {
            var messages = snapshot.Messages
                .Select(message => new MessageSeed(message.Role, message.Author, message.Content, message.Timestamp))
                .ToList();

            return new ConversationSeed
            {
                Id = snapshot.Id,
                Title = snapshot.Title,
                Preview = snapshot.Preview,
                UpdatedAt = snapshot.UpdatedAt,
                IsPersisted = snapshot.IsPersisted,
                IsDraft = snapshot.IsDraft,
                SessionId = snapshot.SessionId,
                Messages = messages,
                ViewMessages = messages
                    .Select(message => message.ToViewModel())
                    .ToList()
            };
        }

        private void SelectConversation(string id, bool keepMessages = false)
        {
            if (!_conversations.TryGetValue(id, out var conversation))
            {
                return;
            }

            _selectedConversationId = id;
            RefreshConversationGroups();
            RaiseBusyStateChanged();

            if (keepMessages)
            {
                return;
            }

            Messages.Clear();
            foreach (var message in conversation.ViewMessages)
            {
                Messages.Add(message);
            }
        }

        private void ClearSelection()
        {
            _selectedConversationId = null;
            Messages.Clear();
            RefreshConversationGroups();
            RaisePropertyChanged(nameof(SelectedConversationId));
            RaisePropertyChanged(nameof(EmptyStateTitle));
            RaisePropertyChanged(nameof(EmptyStateDescription));
        }

        private void RefreshConversationGroups()
        {
            var grouped = _conversations.Values
                .OrderByDescending(conversation => conversation.UpdatedAt)
                .GroupBy(conversation => GetGroupTitle(conversation.UpdatedAt))
                .ToList();

            ConversationGroups.Clear();
            SidebarItems.Clear();
            foreach (var group in grouped)
            {
                var groupViewModel = new ConversationGroupViewModel(group.Key);
                foreach (var conversation in group)
                {
                    var item = new ConversationListItemViewModel
                    {
                        Id = conversation.Id,
                        Title = conversation.Title,
                        Preview = conversation.Preview,
                        UpdatedAt = conversation.UpdatedAt,
                        IsSelected = conversation.Id == _selectedConversationId,
                        IsProcessing = conversation.IsProcessing
                    };

                    groupViewModel.Items.Add(item);
                    SidebarItems.Add(item);
                }

                ConversationGroups.Add(groupViewModel);
            }

            RaisePropertyChanged(nameof(SelectedConversationId));
        }

        private void RaiseBusyStateChanged()
        {
            RaisePropertyChanged(nameof(CanSend));
            RaisePropertyChanged(nameof(SendButtonText));
            RaisePropertyChanged(nameof(IsBusyIndicatorActive));
            RaisePropertyChanged(nameof(ActivityBadgeVisibility));
            RaisePropertyChanged(nameof(ActivityBadgeText));
            RaisePropertyChanged(nameof(ComposerStatusText));
        }

        private bool TryGetSelectedConversation(out ConversationSeed conversation)
        {
            conversation = null!;
            return _selectedConversationId is not null
                && _conversations.TryGetValue(_selectedConversationId, out conversation!);
        }

        private bool IsConversationSelected(ConversationSeed conversation)
            => string.Equals(conversation.Id, _selectedConversationId, StringComparison.Ordinal);

        private void AddMessageToConversation(ConversationSeed conversation, ChatMessageViewModel message)
        {
            conversation.ViewMessages.Add(message);
            if (IsConversationSelected(conversation))
            {
                Messages.Add(message);
            }
        }

        private void SetConversationProcessing(ConversationSeed conversation, bool isProcessing)
        {
            conversation.IsProcessing = isProcessing;
            conversation.RuntimeActivityText = isProcessing ? null : null;

            // Update the matching sidebar item
            foreach (var item in SidebarItems)
            {
                if (string.Equals(item.Id, conversation.Id, StringComparison.Ordinal))
                {
                    item.IsProcessing = isProcessing;
                    break;
                }
            }

            if (IsConversationSelected(conversation))
            {
                RaiseBusyStateChanged();
            }
        }

        private void ApplySetupState(ShellSetupState setupState)
        {
            _setupState = setupState;

            ReplaceItems(SetupBlockers, setupState.Blockers);
            ReplaceItems(SetupPrerequisites, setupState.Prerequisites);

            RaisePropertyChanged(nameof(SetupCardVisibility));
            RaisePropertyChanged(nameof(RequiresSetupPrompt));
            RaisePropertyChanged(nameof(SetupTitle));
            RaisePropertyChanged(nameof(SetupSummaryText));
            RaisePropertyChanged(nameof(SetupPackageReferenceText));
            RaisePropertyChanged(nameof(SetupWorkspaceText));
            RaisePropertyChanged(nameof(SetupMcpConfigText));
            RaisePropertyChanged(nameof(SetupEulaUrl));
            RaisePropertyChanged(nameof(SetupAuthenticationCommandText));
            RaisePropertyChanged(nameof(EulaStepStatusText));
            RaisePropertyChanged(nameof(AuthStepStatusText));
            RaisePropertyChanged(nameof(EulaStepDescription));
            RaisePropertyChanged(nameof(AuthStepDescription));
        }

        private static void ReplaceItems<T>(ObservableCollection<T> target, IReadOnlyList<T> items)
        {
            target.Clear();
            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private string ResolveStreamingStatusText()
            => ConnectionBadgeText switch
            {
                RuntimeConnectionBadge => "Streaming WorkICQ response…",
                WorkIqBlockedConnectionBadge => "Showing WorkICQ blocking error…",
                _ => "Streaming placeholder response…"
            };

        private static string BuildTitle(string prompt)
        {
            const int maxLength = 42;
            var singleLine = prompt.Replace(Environment.NewLine, " ").Trim();
            return singleLine.Length <= maxLength ? singleLine : string.Concat(singleLine.Substring(0, maxLength).TrimEnd(), "…");
        }

        private static string GetGroupTitle(DateTime timestamp)
        {
            var today = DateTime.Now.Date;
            if (timestamp.Date == today)
            {
                return "Today";
            }

            if (timestamp.Date == today.AddDays(-1))
            {
                return "Yesterday";
            }

            if (timestamp.Date >= today.AddDays(-6))
            {
                return "This week";
            }

            return "Earlier";
        }

        private static string FormatConversationTimestamp(DateTime timestamp)
        {
            var today = DateTime.Now.Date;
            if (timestamp.Date == today)
            {
                return timestamp.ToString("t");
            }

            if (timestamp.Date == today.AddDays(-1))
            {
                return "yesterday";
            }

            return timestamp.ToString("m");
        }

        private sealed class ConversationSeed
        {
            public string Id { get; set; } = string.Empty;

            public string Title { get; set; } = "New chat";

            public string Preview { get; set; } = "Waiting for your first question";

            public DateTime UpdatedAt { get; set; }

            public bool IsPersisted { get; set; }

            public bool IsDraft { get; set; }

            public string? SessionId { get; set; }

            public bool IsProcessing { get; set; }

            public bool IsSendingPhase { get; set; }

            public string? RuntimeActivityText { get; set; }

            public CancellationTokenSource? StreamingCts { get; set; }

            public List<MessageSeed> Messages { get; set; } = new List<MessageSeed>();

            public List<ChatMessageViewModel> ViewMessages { get; set; } = new List<ChatMessageViewModel>();
        }

        private sealed class MessageSeed
        {
            public MessageSeed(ChatRole role, string author, string content, DateTime timestamp)
            {
                Role = role;
                Author = author;
                Content = content;
                Timestamp = timestamp;
            }

            public ChatRole Role { get; }

            public string Author { get; }

            public string Content { get; }

            public DateTime Timestamp { get; }

            public ChatMessageViewModel ToViewModel()
                => new ChatMessageViewModel(Role, Author, Content, Timestamp);
        }
    }
}
