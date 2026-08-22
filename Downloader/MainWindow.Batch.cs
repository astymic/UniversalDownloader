using System;
using System.Windows;
using UniversalDownloader.Controls;

namespace UniversalDownloader
{
    public partial class MainWindow
    {
        private void BatchImportButton_Click(object sender, RoutedEventArgs e)
        {
            string defaultFolder = SelectedDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            var (confirmed, items) = BatchImportDialog.Show(defaultFolder, this);

            if (confirmed && items.Count > 0)
            {
                foreach (var item in items)
                {
                    _queueManager.Enqueue(item);
                }

                ModernMessageBox.Show($"Added {items.Count} items to download queue!", "Batch Import", MessageBoxButton.OK, MessageBoxImage.Information, this);
            }
        }
    }
}
