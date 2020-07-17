namespace ExpirationDateNotifier.Configuration
{
    public class EventGridConfiguration
    {
        public string ExpiringSecretEventType { get; set; }
        public string ExpiringCertificateEventType { get; set; }
    }
}