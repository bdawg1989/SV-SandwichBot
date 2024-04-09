using PKHeX.Core;
using SysBot.Base;
using System.Threading;

namespace SysBot.Pokemon
{
    public interface IPokeBotRunner
    {
        PokeSandwichHubConfig Config { get; }
        bool RunOnce { get; }
        bool IsRunning { get; }

        void StartAll();

        void StopAll();

        void InitializeStart();

        void Add(PokeRoutineExecutorBase newbot);

        void Remove(IConsoleBotConfig state, bool callStop);

        BotSource<PokeBotState>? GetBot(PokeBotState state);

        PokeRoutineExecutorBase CreateBotFromConfig(PokeBotState cfg);

        bool SupportsRoutine(PokeRoutineType pokeRoutineType);
    }

    public abstract class PokeBotRunner<T> : BotRunner<PokeBotState>, IPokeBotRunner where T : PKM, new()
    {
        public readonly PokeSandwichHub<T> Hub;
        private readonly BotFactory<T> Factory;

        public PokeSandwichHubConfig Config => Hub.Config;

        protected PokeBotRunner(PokeSandwichHub<T> hub, BotFactory<T> factory)
        {
            Hub = hub;
            Factory = factory;
        }

        protected PokeBotRunner(PokeSandwichHubConfig config, BotFactory<T> factory)
        {
            Factory = factory;
            Hub = new PokeSandwichHub<T>(config);
        }

        protected virtual void AddIntegrations()
        { }

        public override void Add(RoutineExecutor<PokeBotState> bot)
        {
            base.Add(bot);
            if (bot is PokeRoutineExecutorBase b && b.Config.InitialRoutine.IsSandwichBot())
                Hub.Bots.Add(b);
        }

        public override bool Remove(IConsoleBotConfig cfg, bool callStop)
        {
            var bot = GetBot(cfg)?.Bot;
            if (bot is PokeRoutineExecutorBase b && b.Config.InitialRoutine.IsSandwichBot())
                Hub.Bots.Remove(b);
            return base.Remove(cfg, callStop);
        }

        public override void StartAll()
        {
            InitializeStart();

            if (!Hub.Config.SkipConsoleBotCreation)
                base.StartAll();
        }

        public override void InitializeStart()
        {
            if (RunOnce)
                return;

            AddIntegrations();

            base.InitializeStart();
        }

        public override void StopAll()
        {
            base.StopAll();

            // bots currently don't de-register
            Thread.Sleep(100);
        }

        public override void PauseAll()
        {
            if (!Hub.Config.SkipConsoleBotCreation)
                base.PauseAll();
        }

        public override void ResumeAll()
        {
            if (!Hub.Config.SkipConsoleBotCreation)
                base.ResumeAll();
        }

        public PokeRoutineExecutorBase CreateBotFromConfig(PokeBotState cfg) => Factory.CreateBot(Hub, cfg);

        public BotSource<PokeBotState>? GetBot(PokeBotState state) => base.GetBot(state);

        void IPokeBotRunner.Remove(IConsoleBotConfig state, bool callStop) => Remove(state, callStop);

        public void Add(PokeRoutineExecutorBase newbot) => Add((RoutineExecutor<PokeBotState>)newbot);

        public bool SupportsRoutine(PokeRoutineType t) => Factory.SupportsRoutine(t);
    }
}