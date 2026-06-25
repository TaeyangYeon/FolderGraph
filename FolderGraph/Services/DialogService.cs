using System.Windows;
using FolderGraph.Services.Abstractions;

namespace FolderGraph.Services
{
    /// <summary>WPF MessageBox 기반 다이얼로그 구현.</summary>
    public class DialogService : IDialogService
    {
        public bool Confirm(string message, string title)
        {
            MessageBoxResult result = MessageBox.Show(
                message, title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            return result == MessageBoxResult.Yes;
        }

        public void ShowError(string message, string title)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
