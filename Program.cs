using System.Diagnostics;
using System.Text.RegularExpressions;
using Discord;
using Discord.Gateway;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using File = System.IO.File;

try
{
    string[] tokens = File.ReadAllLines("Tokens.txt");
    string[] rawChannelsId = File.ReadAllLines("Channels.txt");
    ulong[] channelsId = new ulong[rawChannelsId.Length];
    int iteratorChannels = -1;
    for (int i = 0; i < rawChannelsId.Length; i++)
        channelsId[i] = Convert.ToUInt64(rawChannelsId[i]);

    Console.Write("Задержка между ответами на сообщения (в миллисекундах): ");
    int milliseconds = Convert.ToInt32(Console.ReadLine());

    string[] messages = File.ReadAllLines("Messages.txt");
    (string answer, string question)[] answerQuestion = new (string, string)[messages.Length];
    for (int i = 0; i < messages.Length; i++)
    {
        string[] splittedMessage = messages[i].Split('|');
        if (splittedMessage.Length > 1)
        {
            answerQuestion[i].answer = splittedMessage[0].Trim();
            answerQuestion[i].question = splittedMessage[1].Trim();
        }
    }

    Dictionary<int, (string token, ulong guildId, ulong channelId, ulong messageId)> telegramMessageIdDiscordData = new();
    Console.Write("Токен Телеграм-бота: ");
    var telegramClient = new TelegramBotClient(Console.ReadLine() ?? "");
    telegramClient.StartReceiving(
        HandleUpdateAsync,
        (telegramClient, exception, cancellationToken) =>
        {
            Console.WriteLine(exception.Message);
            return Task.CompletedTask;
        });

    Console.Write("Название Телеграм-канала, в который будут приходить сообщения: ");
    string telegramChannelName = Console.ReadLine() ?? "";


    async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        if (update.Type != UpdateType.ChannelPost)
            return;
        
        var chatId = update.ChannelPost.Chat.Id;
        var message = update.ChannelPost;

        if (message.Text == "!отключить")
        {
            await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Приложение отключено!",
            cancellationToken: cancellationToken);
            Environment.Exit(0);
        }

        if (update.ChannelPost.ReplyToMessage == null)
            return;

        var (token, guildId, channelId, messageId) = telegramMessageIdDiscordData[message.ReplyToMessage.MessageId];

        DiscordClient client = new(token);
        await Task.Run(async () =>
        {
            await client.TriggerTypingAsync(channelId);
            await Task.Delay(3500);
            await client.SendMessageAsync(channelId, new MessageProperties() { Content = message.Text, ReplyTo = new MessageReference(guildId, messageId) });
        }, cancellationToken);

        Message sentMessage = await botClient.SendTextMessageAsync(
            chatId: chatId,
            text: "Сообщение отправлено!",
            cancellationToken: cancellationToken);
    }

    Parallel.ForEach(tokens, token =>
    {
        ulong channelId = channelsId[Interlocked.Increment(ref iteratorChannels)];
        DiscordSocketClient client = new();
        Stopwatch stopwatch = new();
        client.OnMessageReceived += async (discordClient, args) =>
        {
            var message = args.Message;

            if (discordClient.User.Id == message.Author.User.Id) return;

            if (message.Mentions.FirstOrDefault(m => m.Id == discordClient.User.Id) != null)
            {
                var telegramMessage = await telegramClient.SendTextMessageAsync(new ChatId(telegramChannelName), $"Сервер: `{(await client.GetGuildAsync(message.Guild.Id)).Name}`\nКанал: `{(await client.GetChannelAsync(message.Channel.Id)).Name}`\nПользователь: `{message.Author.User.Username}`\nСообщение: `{message.Content}`", ParseMode.MarkdownV2);
                telegramMessageIdDiscordData.Add(telegramMessage.MessageId, (token, message.Guild.Id, message.Channel.Id, message.Id));
            }
            if (stopwatch.ElapsedMilliseconds > milliseconds)
            {
                if (message.Channel.Id == channelId)
                {
                    foreach (var (answer, question) in answerQuestion)
                    {
                        if (answer != null && question != null)
                        {
                            var content = message.Content.Insert(0, " ") + " ";
                            var questionSplitted = question.Split(' ').Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                            for (int i = 0; i < questionSplitted.Length; i++)
                            {
                                var result = Regex.Replace(questionSplitted[i], @"\W", "").Insert(0, " ") + " ";
                                if (result != "  ")
                                {
                                    questionSplitted[i] = result;
                                }
                            }
                            if (questionSplitted.Where(word => word.Length > 1 && content.Contains(word, StringComparison.OrdinalIgnoreCase)).Count() > 1)
                            {
                                await client.TriggerTypingAsync(channelId);
                                Thread.Sleep(3500);
                                stopwatch.Restart();
                                await discordClient.SendMessageAsync(channelId, new MessageProperties() { Content = answer, ReplyTo = new MessageReference(message.Guild.Id, message.Id) });
                                break;
                            }
                        }
                    }
                }
            }
        };
        client.OnLoggedIn += (_, __) =>
        {
            stopwatch.Start();
        };
        client.Login(token);
        Thread.Sleep(-1);
    });
}
catch (Exception ex)
{
    Console.WriteLine(ex.Message);
    Console.ReadLine();
}