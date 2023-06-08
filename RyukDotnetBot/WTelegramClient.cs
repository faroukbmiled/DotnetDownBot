namespace RyukDotNetBot
{
    internal class WTelegramClient
    {
        private Func<string, string> config;

        public WTelegramClient(Func<string, string> config)
        {
            this.config = config;
        }
    }
}