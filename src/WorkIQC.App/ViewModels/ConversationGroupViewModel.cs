using System.Collections.ObjectModel;

namespace WorkIQC.App.ViewModels
{
    public sealed class ConversationGroupViewModel
    {
        public ConversationGroupViewModel(string title)
        {
            Title = title;
            Items = new ObservableCollection<ConversationListItemViewModel>();
        }

        public string Title { get; private set; }

        public ObservableCollection<ConversationListItemViewModel> Items { get; private set; }
    }
}
