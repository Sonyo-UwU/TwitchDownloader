namespace TwitchDownloaderCore.TwitchObjects.Gql
{

    public class GqlStreamCreatedAtResponse
    {
        public GqlStreamCreatedAtResponseData data { get; set; }
        public Extensions extensions { get; set; }
    }
    public class GqlStreamCreatedAtResponseData
    {
        public GqlStreamCreatedAtResponseUser user { get; set; }
    }
    public class GqlStreamCreatedAtResponseUser
    {
        public GqlStreamCreatedAtResponseStream stream { get; set; }
    }
    public class GqlStreamCreatedAtResponseStream
    {
        public string createdAt { get; set; }
    }
}
