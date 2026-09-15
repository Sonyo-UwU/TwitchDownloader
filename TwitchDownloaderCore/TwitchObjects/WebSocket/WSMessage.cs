using System.Text.Json;

namespace TwitchDownloaderCore.TwitchObjects.WebSocket
{
    public class WSMessage
    {
        public WSMessageMetadata metadata { get; set; }
        public JsonElement payload { get; set; }
    }

    public class WSMessageMetadata
    {
        public enum MessageType
        {
            session_welcome,
            session_keepalive,
            notification,
            session_reconnect,
            revocation
        }

        public string message_id { get; set; }
        public MessageType message_type { get; set; }
        public string message_timestamp { get; set; }
    }

    public class WSMessagePayloadWelcome
    {
        public WSMessagePayloadWelcomeSession session { get; set; }

        public class WSMessagePayloadWelcomeSession
        {
            public string id { get; set; }
            public string status { get; set; }
            public string connected_at { get; set; }
            public int keepalive_timeout_seconds { get; set; }
            public string reconnect_url { get; set; }
        }
    }
}
