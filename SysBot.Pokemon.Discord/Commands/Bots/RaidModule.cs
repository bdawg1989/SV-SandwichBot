using Discord;
using Discord.Commands;
using Discord.WebSocket;
using PKHeX.Core;

namespace SysBot.Pokemon.Discord.Commands.Bots
{
    [Summary("Generates and queues various silly trade additions")]
    public partial class SandwichModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private readonly PokeSandwichHub<T> Hub = SysCord<T>.Runner.Hub;
        private static DiscordSocketClient _client => SysCord<T>.Instance.GetClient();

    }
}