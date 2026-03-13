using Microsoft.UI.Xaml.Media;
using WorkIQC.App.Services;

namespace WorkIQC.App.ViewModels
{
    public sealed class ConversationListItemViewModel : ObservableObject
    {
        private bool _isSelected;

        public string Id { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Preview { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (!SetProperty(ref _isSelected, value))
                {
                    return;
                }

                RaisePropertyChanged(nameof(BackgroundBrush));
                RaisePropertyChanged(nameof(BorderBrush));
                RaisePropertyChanged(nameof(TitleBrush));
                RaisePropertyChanged(nameof(PreviewBrush));
            }
        }

        public Brush BackgroundBrush => ThemeBrushResolver.GetBrush(IsSelected ? "SidebarItemSelectedBackgroundBrush" : "SidebarItemBackgroundBrush");

        public Brush BorderBrush => ThemeBrushResolver.GetBrush(IsSelected ? "SidebarItemSelectedBorderBrush" : "SidebarItemBorderBrush");

        public Brush TitleBrush => ThemeBrushResolver.GetBrush("TextPrimaryBrush");

        public Brush PreviewBrush => ThemeBrushResolver.GetBrush(IsSelected ? "SidebarItemSelectedPreviewBrush" : "TextSecondaryBrush");

        public string TimestampLabel
        {
            get
            {
                var now = DateTime.Now.Date;
                var updated = UpdatedAt.Date;

                if (updated == now)
                {
                    return UpdatedAt.ToString("h:mm tt");
                }

                if (updated == now.AddDays(-1))
                {
                    return "Yesterday";
                }

                if (updated >= now.AddDays(-6))
                {
                    return UpdatedAt.ToString("ddd");
                }

                return UpdatedAt.ToString("MMM d");
            }
        }

        public void RefreshTheme()
        {
            RaisePropertyChanged(nameof(BackgroundBrush));
            RaisePropertyChanged(nameof(BorderBrush));
            RaisePropertyChanged(nameof(TitleBrush));
            RaisePropertyChanged(nameof(PreviewBrush));
        }
    }
}
