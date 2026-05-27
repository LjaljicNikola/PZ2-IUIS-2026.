using Notification.Wpf;

namespace NetworkService.Helpers
{
    public class ToastNotification
    {
        public string Title { get; set; }
        public string Message { get; set; }
        public NotificationType Type { get; set; }

        public ToastNotification()
        {

        }

        public ToastNotification(string title, string message, NotificationType type)
        {
            Title = title;
            Message = message;
            Type = type;
        }
    }
}
