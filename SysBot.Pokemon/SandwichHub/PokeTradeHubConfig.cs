using System.ComponentModel;

// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global

namespace SysBot.Pokemon
{
    public sealed class PokeSandwichHubConfig : BaseConfig
    {
        private const string BotSandwich = nameof(BotSandwich);
        private const string Integration = nameof(Integration);

        [Category(Operation), Description("Add extra time for slower Switches.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public TimingSettings Timings { get; set; } = new();

        [Category(BotSandwich), Description("Name of the Discord Bot the Program is Running. This will Title the window for easier recognition. Requires program restart.")]
        public string BotName { get; set; } = string.Empty;

        [Browsable(false)]
        [Category(Integration), Description("Users Theme Option Choice.")]
        public string ThemeOption { get; set; } = string.Empty;

        [Category(BotSandwich)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public RotatingSandwichSettingsSV RotatingSandwichSV { get; set; } = new();

        [Category(BotSandwich), Description("Stop conditions for EggBot, FossilBot, and EncounterBot.")]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public StopConditionSettings StopConditions { get; set; } = new();


        // Integration

        [Category(Integration)]
        [TypeConverter(typeof(ExpandableObjectConverter))]
        public DiscordSettings Discord { get; set; } = new();
    }
}