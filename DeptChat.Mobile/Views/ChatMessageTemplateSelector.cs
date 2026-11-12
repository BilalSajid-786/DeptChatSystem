using DeptChat.Mobile.Services;
using DeptChat.Mobile.ViewModels;

namespace DeptChat.Mobile.Views;

/// <summary>
/// Picks the chat row layout for a message, so each row only builds the views its type needs
/// (a text message no longer carries a hidden image, document box, play button and slider).
/// Deleted messages have their type flags cleared and so use the text layout, which shows the
/// "This message was deleted" label.
/// </summary>
public class ChatMessageTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? ImageTemplate { get; set; }
    public DataTemplate? DocumentTemplate { get; set; }
    public DataTemplate? VoiceTemplate { get; set; }

    protected override DataTemplate OnSelectTemplate(object item, BindableObject container)
    {
        if (item is ChatMessageItem message)
        {
            if (message.IsVoiceMessage && VoiceTemplate != null)
            {
                PerfTrace.RowShown("voice");
                return VoiceTemplate;
            }

            if (message.IsImageMessage && ImageTemplate != null)
            {
                PerfTrace.RowShown("image");
                return ImageTemplate;
            }

            if (message.IsDocumentMessage && DocumentTemplate != null)
            {
                PerfTrace.RowShown("document");
                return DocumentTemplate;
            }
        }

        PerfTrace.RowShown("text");
        return TextTemplate ?? ImageTemplate ?? DocumentTemplate ?? VoiceTemplate
            ?? throw new InvalidOperationException("ChatMessageTemplateSelector has no templates set.");
    }
}
