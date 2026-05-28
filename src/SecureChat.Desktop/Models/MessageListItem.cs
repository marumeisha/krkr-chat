namespace SecureChat.Desktop.Models;

public sealed class MessageListItem
{
	public MessageListItem(string summary, string detail, MessageAttachmentPayload? attachment = null)
	{
		Summary = summary;
		Detail = detail;
		Attachment = attachment;
	}

	public string Summary { get; }

	public string Detail { get; }

	public MessageAttachmentPayload? Attachment { get; }

	public bool HasAttachment => Attachment is not null;
}