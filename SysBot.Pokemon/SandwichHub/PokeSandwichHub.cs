using PKHeX.Core;
using System.Collections.Concurrent;

namespace SysBot.Pokemon
{
    /// <summary>
    /// Centralizes logic for Sandwich bot coordination.
    /// </summary>
    /// <typeparam name="T">Type of <see cref="PKM"/> to distribute.</typeparam>
    public class PokeSandwichHub<T> where T : PKM, new()
    {
        public RotatingSandwichSettingsSV RotatingSandwichSV { get; set; }

        public PokeSandwichHub(PokeSandwichHubConfig config)
        {
            Config = config;
        }

        public readonly PokeSandwichHubConfig Config;

        /// <summary> Sandwich Bots only, used to delegate multi-player tasks </summary>
        public readonly ConcurrentPool<PokeRoutineExecutorBase> Bots = new();

        public bool SandwichBotsReady => !Bots.All(z => z.Config.CurrentRoutineType == PokeRoutineType.Idle);
    }
}