using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using TL;
using WTelegram;

namespace RyukDotNetBot
{
    class Program
    {
        private static WTelegram.Client _client;
        private static Messages_Chats chats;
        private static Dictionary<long, User> _users = new();
        private static Dictionary<long, ChatBase> _chats = new();

        static async Task Main(string[] args)
        {
            StreamWriter WTelegramLogs = new StreamWriter("ryuk.log", true, Encoding.UTF8) { AutoFlush = true };
            WTelegram.Helpers.Log = (lvl, str) => WTelegramLogs.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{"TDIWE!"[lvl]}] {str}");
            string folder_name = args.Length > 2 ? args[args.Length - 1] : "worker";
            string destinationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory + folder_name);
            _client = new WTelegram.Client(Config);
            await _client.LoginUserIfNeeded();
            chats = await _client.Messages_GetAllChats();
            var dialogs = await _client.Messages_GetAllDialogs();
            if (args[0] == "getall")
            {
                foreach (ChatBase chat in chats.chats.Values)
                {
                    if (chat.IsActive)
                    {
                        Console.WriteLine(chat.ID + " || " + (chat.MainUsername == null ? "".PadRight(50) 
                            : chat.MainUsername.Trim().PadRight(50)) + " - " + chat.Title);
                    }
                }
                foreach (var privatechats in dialogs.users.Values)
                {
                    Console.WriteLine(privatechats.id + " || " + (privatechats.MainUsername == null ? "".PadRight(50) : 
                        privatechats.MainUsername.Trim().PadRight(50)) + " - " + privatechats.first_name);
                }
            }
            else
            {
                await DownloadFilesFromGroup(destinationPath, args);
            }
            _client.Dispose();
        }

        static string Config(string what)
        {
            string configFile = "config.conf";
            string[] lines = File.ReadAllLines(configFile);

            foreach (string line in lines)
            {
                string[] parts = line.Split('=');
                if (parts.Length == 2 && parts[0] == what)
                {
                    return parts[1];
                }
            }

            return null;
        }

        private static async Task DownloadFilesFromGroup(string destinationPath, string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Please provide the chat or message ID as the first argument.");
                return;
            }

            Stopwatch stopwatch = new Stopwatch();
            string chatId = args[0];
            int messageLimit = args.Length >= 2 ? int.Parse(args[1]) : 100;
            string pathProgram = destinationPath;

            ChatBase group = chats.chats.Values.FirstOrDefault(
                chat =>
                {
                    if (chat is Chat c)
                    {
                        return c.id.ToString() == chatId;
                    }
                    else if (chat is TL.Channel channel)
                    {
                        return channel.id.ToString() == chatId;
                    }
                    return false;
                }
            );

            if (group != null)
            {
                Console.WriteLine("Chat found: " + group.Title);
                if (!Directory.Exists(destinationPath))
                {
                    Directory.CreateDirectory(destinationPath);
                }

                bool useOffsetId = messageLimit > 100;
                int offsetId = 0;
                int remainingLimit = messageLimit;
                int currentFileNumber = 0;
                int DownloadedFiles = 0;
                int SkippedFiles = 0;

                do
                {
                    int currentLimit = Math.Min(remainingLimit, 100);
                    Messages_MessagesBase messages = null;

                    if (useOffsetId)
                    {
                        messages = await _client.Messages_GetHistory(group, offsetId, DateTime.Now, 0, currentLimit);
                    }
                    else
                    {
                        messages = await _client.Messages_GetHistory(group, 0, DateTime.Now, 0, currentLimit);
                    }

                    int totalFiles = messages.Messages.Count(msgBase =>
                    {
                        if (msgBase is TL.Message msg && msg.media is MessageMediaDocument { document: Document document })
                        {
                            if (document.mime_type == "text/plain")
                            {
                                return true;
                            }
                        }
                        return false;
                    });

                    foreach (var msgBase in messages.Messages)
                    {    
                        if (
                            msgBase is TL.Message msg
                            && msg.media is MessageMediaDocument { document: Document document }
                        )
                        {
                            if (document.mime_type == "text/plain")
                            {
                                currentFileNumber++;
                                string filePath = Path.Combine(destinationPath, document.Filename);

                                if (File.Exists(filePath))
                                {
                                    FileInfo fileInfo = new FileInfo(filePath);
                                    if (fileInfo.Length == document.size)
                                    {
                                        Console.WriteLine("File " + document.Filename + " exists, skipping...");
                                        SkippedFiles += 1;
                                        continue;
                                    }
                                    else
                                    {
                                        Console.WriteLine("File " + document.Filename + " exists but the size is different, redownloading...");
                                        File.Delete(filePath);
                                    }
                                }

                                using FileStream fileStream = File.Create(filePath);

                                Task downloadTask = _client.DownloadFileAsync(document, fileStream);
                                Console.WriteLine("Downloading file: " + document.Filename);
                                long previousBytesDownloaded = 0;
                                Stopwatch speedStopwatch = new Stopwatch();
                                stopwatch.Start();
                                double currentSpeed = 0;

                                while (!downloadTask.IsCompleted)
                                {
                                    double progress = (double)fileStream.Length / document.size;
                                    int progressBarLength = 20;
                                    int filledLength = (int)(progress * progressBarLength);
                                    filledLength = Math.Min(filledLength, progressBarLength);
                                    int emptyLength = progressBarLength - filledLength;
                                    Console.Write($"\rProgress: [{new string('#', filledLength)}{new string('-', emptyLength)}] {progress:P} ({currentFileNumber}/{totalFiles}), " +
                                        $"Size: {FormatFileSize(document.size)}, Speed: {FormatFileSize((long)currentSpeed)}/s");
                                    speedStopwatch.Start();

                                    double bytesDownloaded = fileStream.Length - previousBytesDownloaded;
                                    double elapsedSeconds = speedStopwatch.Elapsed.TotalSeconds;
                                    if (elapsedSeconds >= 1)
                                    {
                                        currentSpeed = bytesDownloaded / elapsedSeconds;
                                        previousBytesDownloaded = fileStream.Length;
                                        previousBytesDownloaded = fileStream.Length;
                                        speedStopwatch.Restart();
                                    }

                                }
                                Console.Write($"\rProgress: [{new string('#', 20)}] 100% ({currentFileNumber}/{totalFiles}), Size: {FormatFileSize(document.size)}\n");
                                Console.WriteLine();
                                fileStream.Close();
                                Console.WriteLine("Downloaded file: " + filePath);
                                DownloadedFiles += 1;
                            }
                        }
                        await Task.Delay(100);
                    }

                    remainingLimit -= currentLimit;

                    if (!messages.Messages.Any() || remainingLimit <= 0)
                    {
                        break;
                    }

                    if (useOffsetId)
                    {
                        offsetId = messages.Messages.Last().ID;
                    }
                }
                while (useOffsetId);
                if (DownloadedFiles == 0 && SkippedFiles != 0)
                {
                    Console.WriteLine("All files from group/chat already downloaded!");
                }
                else if (SkippedFiles != 0)
                {
                    Console.WriteLine($"Done downloading {DownloadedFiles} file(s), and skipped {SkippedFiles} existing file(s)");
                }
                else if (SkippedFiles == 0 && DownloadedFiles == 0)
                {
                    Console.WriteLine("No files found to download!");
                }
                else
                {
                    Console.WriteLine($"Done downloading {DownloadedFiles} file(s)!");
                }

            }
            else if (group == null)
            {
                var dialogs = await _client.Messages_GetAllDialogs();
                dialogs.CollectUsersChats(_users, _chats);
                long desiredUserId = long.Parse(chatId);
                int currentFileNumber = 0;
                int DownloadedFiles = 0;
                int SkippedFiles = 0;
                var userDialog = dialogs.dialogs.FirstOrDefault(
                    d => d.Peer is TL.PeerUser peerUser && peerUser.user_id == desiredUserId
                );

                if (userDialog != null)
                {
                    var userPeer = dialogs.UserOrChat(userDialog.Peer);
                    if (userPeer is User desiredUser)
                    {
                        Console.WriteLine("Found: " + (string.IsNullOrEmpty(desiredUser.first_name) ? desiredUser.username : desiredUser.first_name));
                        bool useOffsetId = messageLimit > 100;
                        int remainingLimit = messageLimit;
                        int offsetId = 0;

                        do
                        {
                            int currentLimit = Math.Min(remainingLimit, 100);
                            Messages_MessagesBase messages = null;

                            if (useOffsetId)
                            {
                                messages = await _client.Messages_GetHistory(desiredUser, offsetId, DateTime.Now, 0, currentLimit);
                            }
                            else
                            {
                                messages = await _client.Messages_GetHistory(desiredUser, 0, DateTime.Now, 0, currentLimit);
                            }

                            int totalFiles = messages.Messages.Count(msgBase =>
                            {
                                if (msgBase is TL.Message msg && msg.media is MessageMediaDocument { document: Document document })
                                {
                                    if (document.mime_type == "text/plain")
                                    {
                                        return true;
                                    }
                                }
                                return false;
                            });

                            foreach (var msgBase in messages.Messages)
                            {
                                if (
                                    msgBase is TL.Message msg
                                    && msg.media is MessageMediaDocument { document: Document document }
                                )
                                {
                                    if (document.mime_type == "text/plain")
                                    {
                                        currentFileNumber++;
                                        string filePath = Path.Combine(destinationPath, document.Filename);

                                        if (File.Exists(filePath))
                                        {
                                            FileInfo fileInfo = new FileInfo(filePath);
                                            if (fileInfo.Length == document.size)
                                            {
                                                Console.WriteLine("File " + document.Filename + " exists, skipping...");
                                                SkippedFiles += 1;
                                                continue;
                                            }
                                            else
                                            {
                                                Console.WriteLine("File " + document.Filename + " exists but the size is different, redownloading...");
                                                File.Delete(filePath);
                                            }
                                        }

                                        using FileStream fileStream = File.Create(filePath);

                                        Task downloadTask = _client.DownloadFileAsync(document, fileStream);
                                        Console.WriteLine("Downloading file: " + document.Filename);
                                        long previousBytesDownloaded = 0;
                                        Stopwatch speedStopwatch = new Stopwatch();
                                        stopwatch.Start();
                                        double currentSpeed = 0;

                                        while (!downloadTask.IsCompleted)
                                        {
                                            double progress = (double)fileStream.Length / document.size;
                                            int progressBarLength = 20;
                                            int filledLength = (int)(progress * progressBarLength);
                                            filledLength = Math.Min(filledLength, progressBarLength);
                                            int emptyLength = progressBarLength - filledLength;
                                            Console.Write($"\rProgress: [{new string('#', filledLength)}{new string('-', emptyLength)}] {progress:P} ({currentFileNumber}/{totalFiles})," +
                                                $" Size: {FormatFileSize(document.size)}, Speed: {FormatFileSize((long)currentSpeed)}/s");
                                            speedStopwatch.Start();

                                            double bytesDownloaded = fileStream.Length - previousBytesDownloaded;
                                            double elapsedSeconds = speedStopwatch.Elapsed.TotalSeconds;
                                            if (elapsedSeconds >= 1)
                                            {
                                                currentSpeed = bytesDownloaded / elapsedSeconds;
                                                previousBytesDownloaded = fileStream.Length;
                                                previousBytesDownloaded = fileStream.Length;
                                                speedStopwatch.Restart();
                                            }

                                        }
                                        Console.Write($"\rProgress: [{new string('#', 20)}] 100% ({currentFileNumber}/{totalFiles}), Size: {FormatFileSize(document.size)}\n");
                                        Console.WriteLine();
                                        fileStream.Close();
                                        Console.WriteLine("Downloaded file: " + filePath);
                                        DownloadedFiles += 1;
                                    }
                                }
                                await Task.Delay(100);
                            }

                            remainingLimit -= currentLimit;

                            if (!messages.Messages.Any() || remainingLimit <= 0)
                            {
                                break;
                            }

                            if (useOffsetId)
                            {
                                offsetId = messages.Messages.Last().ID;
                            }
                        }
                        while (useOffsetId);
                        if (DownloadedFiles == 0 && SkippedFiles != 0)
                        {
                            Console.WriteLine("All files from group/chat already downloaded!");
                        }
                        else if (SkippedFiles != 0)
                        {
                            Console.WriteLine($"Done downloading {DownloadedFiles} file(s), and skipped {SkippedFiles} existing file(s)");
                        }
                        else if (SkippedFiles == 0 && DownloadedFiles == 0)
                        {
                            Console.WriteLine("No files found to download!");
                        }
                        else
                        {
                            Console.WriteLine($"Done downloading {DownloadedFiles} file(s)!");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Not a valid user id.");
                    }
                }
                else
                {
                    Console.WriteLine("Not a valid id.");
                }
            }
            else
            {
                Console.WriteLine("Something went wrong!");
            }
        }

        private static string FormatFileSize(long fileSize)
        {
            const int scale = 1024;
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            int unitIndex = 0;
            double size = fileSize;

            while (size >= scale && unitIndex < units.Length - 1)
            {
                size /= scale;
                unitIndex++;
            }

            return $"{size:0.##} {units[unitIndex]}";
        }
    }
}
