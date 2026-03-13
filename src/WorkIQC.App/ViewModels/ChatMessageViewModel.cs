using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using WorkIQC.App.Models;
using WorkIQC.App.Services;

namespace WorkIQC.App.ViewModels
{
    public sealed class ChatMessageViewModel : ObservableObject
    {
        private string _content;
        private bool _isStreaming;
        private DateTime _timestamp;

        public ChatMessageViewModel(ChatRole role, string author, string content, DateTime timestamp, bool isStreaming = false)
        {
            Role = role;
            Author = author;
            _content = content;
            _timestamp = timestamp;
            _isStreaming = isStreaming;
        }

        public ChatRole Role { get; private set; }

        public string Author { get; private set; }

        public string Content
        {
            get => _content;
            private set
            {
                if (!SetProperty(ref _content, value))
                {
                    return;
                }

                RaisePropertyChanged(nameof(TypingIndicatorVisibility));
            }
        }

        public DateTime Timestamp
        {
            get => _timestamp;
            private set
            {
                if (!SetProperty(ref _timestamp, value))
                {
                    return;
                }

                RaisePropertyChanged(nameof(TimestampLabel));
                RaisePropertyChanged(nameof(FormattedTimestamp));
            }
        }

        public bool IsStreaming
        {
            get => _isStreaming;
            private set
            {
                if (!SetProperty(ref _isStreaming, value))
                {
                    return;
                }

                RaisePropertyChanged(nameof(StreamingVisibility));
                RaisePropertyChanged(nameof(TypingIndicatorVisibility));
            }
        }

        public HorizontalAlignment BubbleAlignment => Role == ChatRole.User ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        public Brush BubbleBrush => ThemeBrushResolver.GetBrush(Role == ChatRole.User ? "ChatUserBubbleBrush" : "ChatAssistantBubbleBrush");

        public Brush BorderBrush => ThemeBrushResolver.GetBrush(Role == ChatRole.User ? "ChatUserBorderBrush" : "ChatAssistantBorderBrush");

        public Brush RoleBadgeBrush => ThemeBrushResolver.GetBrush(Role == ChatRole.User ? "ChatUserBadgeBrush" : "ChatAssistantBadgeBrush");

        public Brush RoleBadgeForeground => ThemeBrushResolver.GetBrush(Role == ChatRole.User ? "ChatUserBadgeForegroundBrush" : "ChatAssistantBadgeForegroundBrush");

        public Brush HeaderBrush => ThemeBrushResolver.GetBrush("TextPrimaryBrush");

        public Brush BodyBrush => ThemeBrushResolver.GetBrush(Role == ChatRole.User ? "TextPrimaryBrush" : "TextSecondaryBrush");

        public string RoleLabel => Role == ChatRole.User ? "You" : "Assistant";

        public string TimestampLabel => Timestamp.ToString("h:mm tt");

        /// <summary>
        /// Gets a compact formatted timestamp for display in the chat transcript.
        /// </summary>
        public string FormattedTimestamp => Timestamp.ToString("h:mm tt");

        /// <summary>
        /// Gets the Segoe Fluent Icons glyph for the avatar based on role.
        /// </summary>
        public string AvatarGlyph => Role == ChatRole.User ? "\uE77B" : "\uE8BD";

        public Visibility StreamingVisibility => IsStreaming ? Visibility.Visible : Visibility.Collapsed;

        public Visibility TypingIndicatorVisibility => IsStreaming && string.IsNullOrEmpty(Content) ? Visibility.Visible : Visibility.Collapsed;

        public void AppendContent(string chunk)
            => Content += chunk;

        public void CompleteStreaming(DateTime completedAt)
        {
            Timestamp = completedAt;
            IsStreaming = false;
        }

        public ChatMessageViewModel Clone()
            => new ChatMessageViewModel(Role, Author, Content, Timestamp, IsStreaming);

        public void RefreshTheme()
        {
            RaisePropertyChanged(nameof(BubbleBrush));
            RaisePropertyChanged(nameof(BorderBrush));
            RaisePropertyChanged(nameof(RoleBadgeBrush));
            RaisePropertyChanged(nameof(RoleBadgeForeground));
            RaisePropertyChanged(nameof(HeaderBrush));
            RaisePropertyChanged(nameof(BodyBrush));
        }
    }
}
