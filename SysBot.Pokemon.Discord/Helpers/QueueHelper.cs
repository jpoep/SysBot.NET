using Discord;
using Discord.Commands;
using Discord.Net;
using Discord.WebSocket;
using PKHeX.Core;
using System.Threading.Tasks;

namespace SysBot.Pokemon.Discord;

public static class QueueHelper<T> where T : PKM, new()
{
    private const uint MaxTradeCode = 9999_9999;

    public static async Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type, SocketUser trader)
    {
        if ((uint)code > MaxTradeCode)
        {
            await context.Channel.SendMessageAsync("Trade code should be 00000000-99999999!").ConfigureAwait(false);
            return;
        }

        try
        {
            const string helper = "So, jetzt reden wir zwei Schnuckels mal ganz privat. Dein Mon wurde mir gerade von der lieben Dame aus der Pension übergeben, ich trainiere es noch fix für dich.";
            IUserMessage test = await trader.SendMessageAsync(helper).ConfigureAwait(false);

            // Try adding
            var result = AddToTradeQueue(context, trade, code, trainer, sig, routine, type, trader, out var msg);

            // Notify in channel
            await context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            // Notify in PM to mirror what is said in the channel.
            await trader.SendMessageAsync($"{msg}\nDu kannst ja schon mal **{code:0000 0000}** in deine Switch eintippern.").ConfigureAwait(false);

            // Clean Up
            if (result)
            {
                // Delete the user's join message for privacy
                if (!context.IsPrivate)
                    await context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
            else
            {
                // Delete our "I'm adding you!", and send the same message that we sent to the general channel.
                await test.DeleteAsync().ConfigureAwait(false);
            }
        }
        catch (HttpException ex)
        {
            await HandleDiscordExceptionAsync(context, trader, ex).ConfigureAwait(false);
        }
    }

    public static Task AddToQueueAsync(SocketCommandContext context, int code, string trainer, RequestSignificance sig, T trade, PokeRoutineType routine, PokeTradeType type)
    {
        return AddToQueueAsync(context, code, trainer, sig, trade, routine, type, context.User);
    }

    private static bool AddToTradeQueue(SocketCommandContext context, T pk, int code, string trainerName, RequestSignificance sig, PokeRoutineType type, PokeTradeType t, SocketUser trader, out string msg)
    {
        var user = trader;
        var userID = user.Id;
        var name = user.Username;

        var trainer = new PokeTradeTrainerInfo(trainerName, userID);
        var notifier = new DiscordTradeNotifier<T>(pk, trainer, code, user);
        var detail = new PokeTradeDetail<T>(pk, trainer, notifier, t, code, sig == RequestSignificance.Favored);
        var trade = new TradeEntry<T>(detail, userID, type, name);

        var hub = SysCord<T>.Runner.Hub;
        var Info = hub.Queues.Info;
        var added = Info.AddToTradeQueue(trade, userID, sig == RequestSignificance.Owner);

        if (added == QueueResultAdd.AlreadyInQueue)
        {
            msg = "Bre, ich züchte doch schon. Komm wieder, wenn du dein Mon hast.";
            return false;
        }

        var position = Info.CheckPosition(userID, type);

        var ticketID = "";
        if (TradeStartModule<T>.IsStartChannel(context.Channel.Id))
            ticketID = $", unique ID: {detail.ID}";

        var pokeName = "";
        if (t == PokeTradeType.Specific && pk.Species != 0)
            pokeName = $" Receiving: {GameInfo.GetStrings(1).Species[pk.Species]}.";
        var firstPostionMessage = "Es wird auch sofort ready sein, mach dich bereit du Knecht!";
        var laterPositionMessage = $"Allerdings sind da noch {position.Position - 1} ungeduldige TFL-Jungfern vor dir, also schau dir in der Zeit ruhig eine Folge von Frenz an.";
        msg = $"Der PBV fängt an, dein Mon zu breeden, {user.Mention}. {(position.Position == 1 ? firstPostionMessage : laterPositionMessage)}";

        var botct = Info.Hub.Bots.Count;
        if (position.Position > botct)
        {
            var eta = Info.Hub.Config.Queues.EstimateDelay(position.Position, botct);
            msg += $" Estimated: {eta:F1} minutes.";
        }
        return true;
    }

    private static async Task HandleDiscordExceptionAsync(SocketCommandContext context, SocketUser trader, HttpException ex)
    {
        string message = string.Empty;
        switch (ex.DiscordCode)
        {
            case DiscordErrorCode.InsufficientPermissions or DiscordErrorCode.MissingPermissions:
            {
                // Check if the exception was raised due to missing "Send Messages" or "Manage Messages" permissions. Nag the bot owner if so.
                var permissions = context.Guild.CurrentUser.GetPermissions(context.Channel as IGuildChannel);
                if (!permissions.SendMessages)
                {
                    // Nag the owner in logs.
                    message = "You must grant me \"Send Messages\" permissions!";
                    Base.LogUtil.LogError(message, "QueueHelper");
                    return;
                }
                if (!permissions.ManageMessages)
                {
                    var app = await context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
                    var owner = app.Owner.Id;
                    message = $"<@{owner}> You must grant me \"Manage Messages\" permissions!";
                }
            }
                break;
            case DiscordErrorCode.CannotSendMessageToUser:
            {
                // The user either has DMs turned off, or Discord thinks they do.
                message = context.User == trader ? "You must enable private messages in order to be queued!" : "The mentioned user must enable private messages in order for them to be queued!";
            }
                break;
            default:
            {
                // Send a generic error message.
                message = ex.DiscordCode != null ? $"Discord error {(int)ex.DiscordCode}: {ex.Reason}" : $"Http error {(int)ex.HttpCode}: {ex.Message}";
            }
                break;
        }
        await context.Channel.SendMessageAsync(message).ConfigureAwait(false);
    }
}
