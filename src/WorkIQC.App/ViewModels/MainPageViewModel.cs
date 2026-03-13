using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using WorkIQC.App.Models;
using WorkIQC.App.Services;

namespace WorkIQC.App.ViewModels
{
    public sealed class MainPageViewModel : ObservableObject
    {
        private const string RuntimeConnectionBadge = "WorkIQ runtime";
        private const string WorkIqBlockedConnectionBadge = "WorkIQ blocked";
        private readonly Dictionary<string, ConversationSeed> _conversations = new Dictionary<string, ConversationSeed>();
        private readonly IChatShellService _chatShellService;

        private bool _initialized;
        private bool _isSettingsViewActive;
        private bool _isSending;
        private bool _isStreaming;
        private string? _runtimeActivityText;
        private string? _selectedConversationId;
        private ShellSetupState _setupState = new(
            RequiresUserAction: true,
            CanAttemptRuntime: false,
            IsEulaAccepted: false,
            IsAuthenticationHandoffStarted: false,
            SummaryText: "WorkIQ setup has not been checked yet.",
            WorkIQPackageReference: WorkIQC.Runtime.Abstractions.Models.WorkIQRuntimeDefaults.PackageReference,
            WorkspacePath: string.Empty,
            McpConfigPath: string.Empty,
            EulaUrl: WorkIQC.Runtime.Abstractions.Models.WorkIQRuntimeDefaults.EulaUrl,
            EulaMarkerPath: string.Empty,
            AuthenticationMarkerPath: string.Empty,
            AuthenticationCommandLine: WorkIQC.Runtime.Abstractions.Models.WorkIQRuntimeDefaults.CopilotLoginCommand,
            Blockers: Array.Empty<string>(),
            Prerequisites: Array.Empty<string>());
        private string _composerText = string.Empty;
        private string _connectionBadgeText = "Preview data";
        private string _sidebarFooterText = "Local history is ready. Runtime updates appear here when live handoff is active.";

        public MainPageViewModel(IChatShellService chatShellService)
        {
            _chatShellService = chatShellService;

            ConversationGroups = new ObservableCollection<ConversationGroupViewModel>();
            SidebarItems = new ObservableCollection<ConversationListItemViewModel>();
            Messages = new ObservableCollection<ChatMessageViewModel>();
            SetupBlockers = new ObservableCollection<string>();
            SetupPrerequisites = new ObservableCollection<string>();

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

        public ObservableCollection<string> SetupBlockers { get; }

        public ObservableCollection<string> SetupPrerequisites { get; }

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

        public bool CanSend => !IsBusy && !string.IsNullOrWhiteSpace(ComposerText);

        public string SendButtonText => IsBusy ? "Busy" : "Send";

        public string ComposerHintText => "Press Enter to send. Shift+Enter adds a new line.";

        public string ComposerStatusText
            => IsSending
                ? "Sending prompt…"
                : IsStreaming
                    ? _runtimeActivityText ?? ResolveStreamingStatusText()
                    : "Ready to send";

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

        public string SetupTitle => _setupState.RequiresUserAction ? "Complete WorkIQ bootstrap" : "WorkIQ bootstrap is ready";

        public string SetupSummaryText => _setupState.SummaryText;

        public string SetupPackageReferenceText => _setupState.WorkIQPackageReference;

        public string SetupWorkspaceText => _setupState.WorkspacePath;

        public string SetupMcpConfigText => _setupState.McpConfigPath;

        public string SetupEulaUrl => _setupState.EulaUrl;

        public string SetupAuthenticationCommandText => _setupState.AuthenticationCommandLine;

        public string EulaStepStatusText => _setupState.IsEulaAccepted ? "Accepted" : "Required";

        public string AuthStepStatusText => _setupState.IsAuthenticationHandoffStarted ? "Completed" : "Required";

        public string EulaStepDescription => _setupState.IsEulaAccepted
            ? $"Verified during WorkIQ bootstrap and recorded at {_setupState.EulaMarkerPath}."
            : $"Review the terms, then complete the WorkIQ consent bootstrap before the first live session. App-local files alone do not complete consent. Evidence is stored at {_setupState.EulaMarkerPath}.";

        public string AuthStepDescription => _setupState.IsAuthenticationHandoffStarted
            ? $"Copilot auth handoff was recorded locally. This does not prove WorkIQ resolved your identity yet. Evidence is tracked at {_setupState.AuthenticationMarkerPath}."
            : $"Run {SetupAuthenticationCommandText} once during bootstrap before the first live WorkIQ session.";

        public Visibility ActivityBadgeVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

        public bool IsBusyIndicatorActive => IsBusy;

        public string ActivityBadgeText
            => IsSending
                ? "Sending"
                : IsStreaming
                    ? string.IsNullOrWhiteSpace(_runtimeActivityText) ? "Streaming" : "Tool activity"
                    : string.Empty;

        public Visibility TranscriptVisibility => Messages.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility EmptyStateVisibility => Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        public string EmptyStateTitle => _selectedConversationId is null ? "Start a calm new conversation" : "This thread is ready for its first prompt";

        public string EmptyStateDescription
            => _selectedConversationId is null
                ? "Choose a recent thread or begin a new one. The shell already remembers local state and keeps the main workspace centered on your next question."
                : "The draft is selected. Send a first prompt whenever you're ready.";

        private bool IsSending
        {
            get => _isSending;
            set
            {
                if (!SetProperty(ref _isSending, value))
                {
                    return;
                }

                RaiseBusyStateChanged();
            }
        }

        private bool IsStreaming
        {
            get => _isStreaming;
            set
            {
                if (!SetProperty(ref _isStreaming, value))
                {
                    return;
                }

                RaiseBusyStateChanged();
            }
        }

        private bool IsBusy => IsSending || IsStreaming;

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
            Messages.Add(userMessage);
            conversation.Messages.Add(new MessageSeed(ChatRole.User, "You", prompt, now));

            if (conversation.IsDraft)
            {
                conversation.Title = BuildTitle(prompt);
                conversation.IsDraft = false;
            }

            conversation.Preview = prompt;
            conversation.UpdatedAt = now;
            SelectConversation(conversation.Id, keepMessages: true);

            IsSending = true;
            await Task.Delay(260);

            var assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, "WorkIQ", string.Empty, DateTime.Now, isStreaming: true);
            Messages.Add(assistantMessage);

            IsSending = false;
            IsStreaming = true;

            var response = await _chatShellService.SendAsync(
                new ShellSendRequest(conversation.Id, conversation.Title, prompt, conversation.SessionId));

            PromoteConversationIdentity(conversation, response.ConversationId);
            conversation.Title = response.ConversationTitle;
            conversation.IsPersisted = response.IsPersisted;
            conversation.IsDraft = response.IsDraft;
            conversation.SessionId = response.SessionId;
            ConnectionBadgeText = response.ConnectionBadgeText;
            SidebarFooterText = response.SidebarFooterText;

            _runtimeActivityText = null;
            RaisePropertyChanged(nameof(ComposerStatusText));
            RaisePropertyChanged(nameof(ActivityBadgeText));

            var activityTask = ConsumeActivityUpdatesAsync(response.ActivityStream, response.SidebarFooterText);
            await foreach (var chunk in response.ResponseStream)
            {
                assistantMessage.AppendContent(chunk);
            }
            await activityTask;

            var completedAt = DateTime.Now;
            assistantMessage.CompleteStreaming(completedAt);
            conversation.Messages.Add(new MessageSeed(ChatRole.Assistant, "WorkIQ", assistantMessage.Content, completedAt));
            conversation.Preview = assistantMessage.Content.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
                ?? assistantMessage.Content;
            conversation.UpdatedAt = completedAt;
            SelectConversation(conversation.Id, keepMessages: true);

            _runtimeActivityText = null;
            SidebarFooterText = response.SidebarFooterText;
            RaisePropertyChanged(nameof(ComposerStatusText));
            RaisePropertyChanged(nameof(ActivityBadgeText));
            IsStreaming = false;
        }

        public async Task AcceptWorkIqTermsAsync()
            => ApplySetupState(await _chatShellService.AcceptWorkIqTermsAsync());

        public async Task RecordAuthenticationHandoffAsync(string? loginCommand = null)
            => ApplySetupState(await _chatShellService.RecordAuthenticationHandoffAsync(loginCommand));

        public async Task RefreshSetupAsync()
            => ApplySetupState(await _chatShellService.RefreshSetupAsync());

        private async Task ConsumeActivityUpdatesAsync(IAsyncEnumerable<string> activityStream, string baselineSidebarFooter)
        {
            await foreach (var update in activityStream)
            {
                if (string.IsNullOrWhiteSpace(update))
                {
                    continue;
                }

                _runtimeActivityText = update.Trim();
                SidebarFooterText = $"{baselineSidebarFooter} {_runtimeActivityText}";
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
            return new ConversationSeed
            {
                Id = snapshot.Id,
                Title = snapshot.Title,
                Preview = snapshot.Preview,
                UpdatedAt = snapshot.UpdatedAt,
                IsPersisted = snapshot.IsPersisted,
                IsDraft = snapshot.IsDraft,
                SessionId = snapshot.SessionId,
                Messages = snapshot.Messages
                    .Select(message => new MessageSeed(message.Role, message.Author, message.Content, message.Timestamp))
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

            if (keepMessages)
            {
                return;
            }

            Messages.Clear();
            foreach (var message in conversation.Messages.Select(message => message.ToViewModel()))
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
                        IsSelected = conversation.Id == _selectedConversationId
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

        private static void ReplaceItems(ObservableCollection<string> target, IReadOnlyList<string> items)
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
                RuntimeConnectionBadge => "Streaming WorkIQ response…",
                WorkIqBlockedConnectionBadge => "Showing WorkIQ blocking error…",
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
                return timestamp.ToString("h:mm tt");
            }

            if (timestamp.Date == today.AddDays(-1))
            {
                return "yesterday";
            }

            return timestamp.ToString("MMM d");
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

            public List<MessageSeed> Messages { get; set; } = new List<MessageSeed>();
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
