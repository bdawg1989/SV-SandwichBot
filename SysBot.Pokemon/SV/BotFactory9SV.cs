using PKHeX.Core;
using SysBot.Pokemon.SV.BotSandwich;
using System;

namespace SysBot.Pokemon
{
    public class BotFactory9SV : BotFactory<PK9>
    {
        public override PokeRoutineExecutorBase CreateBot(PokeSandwichHub<PK9> Hub, PokeBotState cfg) => cfg.NextRoutineType switch
        {
            PokeRoutineType.RotatingSandwichBot => new RotatingSandwichBotSV(cfg, Hub),
            PokeRoutineType.RemoteControl => new RemoteControlBotSV(cfg),
            _ => throw new ArgumentException(nameof(cfg.NextRoutineType)),
        };

        public override bool SupportsRoutine(PokeRoutineType type) => type switch
        {
            PokeRoutineType.RotatingSandwichBot => true,
            PokeRoutineType.RemoteControl => true,
            _ => false,
        };
    }
}