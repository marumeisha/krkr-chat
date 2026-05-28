using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using SecureChat.Desktop.Models;
using System.IO;

namespace SecureChat.Desktop.Views;

public partial class MessageCardView : UserControl
{
    public MessageCardView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void SaveAttachment_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MessageListItem { Attachment: { } attachment })
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        var storageProvider = topLevel?.StorageProvider;
        if (storageProvider is null || !storageProvider.CanSave)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "保存附件",
            SuggestedFileName = attachment.FileName,
            DefaultExtension = Path.GetExtension(attachment.FileName).TrimStart('.')
        });

        if (file is null)
        {
            return;
        }

        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(attachment.ContentBytes);
    }
}