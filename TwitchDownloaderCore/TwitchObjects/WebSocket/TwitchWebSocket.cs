using System.Net.WebSockets;

namespace TwitchDownloaderCore.TwitchObjects.WebSocket
{
    public struct TwitchWebSocket
    {
        public ClientWebSocket Socket { get; set; }
        public byte[] Buffer { get; set; }
        public string SessionId { get; set; }
    }
}
