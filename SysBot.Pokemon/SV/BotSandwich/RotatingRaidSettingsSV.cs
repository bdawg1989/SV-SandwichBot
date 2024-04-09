using Discord.WebSocket;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;

namespace SysBot.Pokemon
{

    public class RotatingSandwichSettingsSV : IBotStateSettings
    {
        private const string Hosting = nameof(Hosting);
        private const string Counts = nameof(Counts);
        private const string FeatureToggle = nameof(FeatureToggle);

        public override string ToString() => "RotatingSandwichSV Settings";

        [Category(Hosting), Description("Your Active Sandwich List lives here.")]
        public List<RotatingSandwichParameters> ActiveSandwichs { get; set; } = new();

        public RotatingSandwichSettingsCategory SandwichSettings { get; set; } = new RotatingSandwichSettingsCategory();

        public RotatingSandwichPresetFiltersCategory EmbedToggles { get; set; } = new RotatingSandwichPresetFiltersCategory();

        [Category(Hosting), Description("Lobby Options"), DesignerSerializationVisibility(DesignerSerializationVisibility.Content)]
        public LobbyFiltersCategory LobbyOptions { get; set; } = new();

        [Category(Hosting), Description("Users NIDs here are banned Sandwichers.")]
        public RemoteControlAccessList SandwicherBanList { get; set; } = new() { AllowIfEmpty = false };

        public MiscSettingsCategory MiscSettings { get; set; } = new MiscSettingsCategory();

        [Browsable(false)]
        public bool ScreenOff
        {
            get => MiscSettings.ScreenOff;
            set => MiscSettings.ScreenOff = value;
        }

        public class RotatingSandwichParameters
        {
            public override string ToString() => $"{Title}";

            public bool ActiveInRotation { get; set; } = true;

            [Browsable(false)]
            public string[] Description { get; set; } = Array.Empty<string>();

            public Action1Type Action1 { get; set; } = Action1Type.GoAllOut;
            public int Action1Delay { get; set; } = 5;
            public string Title { get; set; } = string.Empty;

            [Browsable(false)]
            public bool AddedByRACommand { get; set; } = false;

            [Browsable(false)]
            public bool SandwichUpNext { get; set; } = false;

            [Browsable(false)]
            public string RequestCommand { get; set; } = string.Empty;

            [Browsable(false)]
            public ulong RequestedByUserID { get; set; }

            [Browsable(false)]
            [System.Text.Json.Serialization.JsonIgnore]
            public SocketUser? User { get; set; }

            [Browsable(false)]
            [System.Text.Json.Serialization.JsonIgnore]
            public List<SocketUser> MentionedUsers { get; set; } = [];
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<EventSettingsCategory>))]
        public class EventSettingsCategory
        {
            public override string ToString() => "Event Settings";

            [Category(Hosting), Description("Set to \"true\" when events are active to properly process level 7 (event) and level 5 (distribution) Sandwichs.")]
            public bool EventActive { get; set; } = false;
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingSandwichSettingsCategory>))]
        public class RotatingSandwichSettingsCategory
        {
            public override string ToString() => "Sandwich Settings";

            [Category(Hosting), Description("Enter the total number of Sandwichs to host before the bot automatically stops. Default is 0 to ignore this setting.")]
            public int TotalSandwichsToHost { get; set; } = 0;

            [Category(Hosting), Description("When enabled, the bot will randomly pick a Sandwich to run, while keeping requests prioritized.")]
            public bool RandomRotation { get; set; } = false;

            [Category(Hosting), Description("When true, bot will add random shiny seeds to queue.  Only User Requests and Mystery Sandwichs will be ran.")]
            public bool MysterySandwichs { get; set; } = false;

            [Category(Hosting), Description("When true, the bot will not allow user requested Sandwichs and will inform them that this setting is on.")]
            public bool DisableRequests { get; set; } = false;

            [Category(Hosting), Description("When true, the bot will allow private Sandwichs.")]
            public bool PrivateSandwichsEnabled { get; set; } = true;

            [Category(Hosting), Description("Limit the number of requests a user can issue.  Set to 0 to disable.\nCommands: $lr <number>")]
            public int LimitRequests { get; set; } = 0;

            [Category(Hosting), Description("Define the time (in minutes) the user must wait for requests once LimitRequests number is reached.  Set to 0 to disable.\nCommands: $lrt <number in minutes>")]
            public int LimitRequestsTime { get; set; } = 0;

            [Category(Hosting), Description("Custom message to display when a user reaches their request limit.")]
            public string LimitRequestMsg { get; set; } = "If you'd like to bypass this limit, please [describe how to get the role].";

            [Category(Hosting), Description("Dictionary of user and role IDs with names that can bypass request limits.\nCommands: $alb @Role or $alb @User")]
            public Dictionary<ulong, string> BypassLimitRequests { get; set; } = new Dictionary<ulong, string>();

            [Category(FeatureToggle), Description("Prevent attacks.  When true, Overworld Spawns (Pokémon) are disabled on the next seed injection.  When false, Overworld Spawns (Pokémon) are enabled on the next seed injection.")]
            public bool DisableOverworldSpawns { get; set; } = true;

            [Category(Hosting), Description("Minimum amount of seconds to wait before starting a Sandwich.")]
            public int TimeToWait { get; set; } = 90;
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<RotatingSandwichPresetFiltersCategory>))]
        public class RotatingSandwichPresetFiltersCategory
        {
            public override string ToString() => "Embed Toggles";

            [Category(Hosting), Description("Sandwich embed description.")]
            public string[] SandwichEmbedDescription { get; set; } = Array.Empty<string>();

            [Category(FeatureToggle), Description("When enabled, the embed will countdown the amount of seconds in \"TimeToWait\" until starting the Sandwich.")]
            public bool IncludeCountdown { get; set; } = true;

            [Category(Hosting), Description("Amount of time (in seconds) to post a requested Sandwich embed.")]
            public int RequestEmbedTime { get; set; } = 30;

            [Category(FeatureToggle), Description("When enabled, the bot will attempt take screenshots for the Sandwich Embeds. If you experience crashes often about \"Size/Parameter\" try setting this to false.")]
            public bool TakeScreenshot { get; set; } = true;

            [Category(Hosting), Description("Delay in milliseconds for capturing a screenshot once in the Sandwich.\n1500 Captures Players Only.\n9000 Captures players and Sandwich Mon.")]
            public ScreenshotTimingOptions ScreenshotTiming { get; set; } = ScreenshotTimingOptions._1500; // default to 1500 ms

            [Category(FeatureToggle), Description("When enabled, the bot will hide the Sandwich code from the Discord embed.")]
            public bool HideSandwichCode { get; set; } = false;
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<LobbyFiltersCategory>))]
        public class LobbyFiltersCategory
        {
            public override string ToString() => "Lobby Filters";

            [Category(Hosting), Description("OpenLobby - Opens the Lobby after x Empty Lobbies\nSkipSandwich - Moves on after x losses/empty Lobbies\nContinue - Continues hosting the Sandwich")]
            public LobbyMethodOptions LobbyMethod { get; set; } = LobbyMethodOptions.SkipSandwich; // Changed the property name here

            [Category(Hosting), Description("Empty Sandwich limit per parameter before the bot hosts an uncoded Sandwich. Default is 3 Sandwichs.")]
            public int EmptySandwichLimit { get; set; } = 3;

            [Category(Hosting), Description("Empty/Lost Sandwich limit per parameter before the bot moves on to the next one. Default is 3 Sandwichs.")]
            public int SkipSandwichLimit { get; set; } = 3;

            [Category(FeatureToggle), Description("Set the action you would want your bot to perform. 'AFK' will make the bot idle, while 'MashA' presses A every 2.5s")]
            public SandwichAction Action { get; set; } = SandwichAction.MashA;

            [Category(FeatureToggle), Description("Delay for the 'MashA' action in seconds.  [3.5 is default]")]
            public double MashADelay { get; set; } = 3.5;  // Default value set to 3.5 seconds
        }

        [Category(Hosting), TypeConverter(typeof(CategoryConverter<MiscSettingsCategory>))]
        public class MiscSettingsCategory
        {
            public override string ToString() => "Misc. Settings";

            [Category(FeatureToggle), Description("When enabled, the screen will be turned off during normal bot loop operation to save power.")]
            public bool ScreenOff { get; set; }

            private int _completedSandwichs;

            [Category(Counts), Description("Sandwichs Started")]
            public int CompletedSandwichs
            {
                get => _completedSandwichs;
                set => _completedSandwichs = value;
            }

            [Category(Counts), Description("When enabled, the counts will be emitted when a status check is requested.")]
            public bool EmitCountsOnStatusCheck { get; set; }

            public int AddCompletedSandwichs() => Interlocked.Increment(ref _completedSandwichs);

            public IEnumerable<string> GetNonZeroCounts()
            {
                if (!EmitCountsOnStatusCheck)
                    yield break;
                if (CompletedSandwichs != 0)
                    yield return $"Started Sandwichs: {CompletedSandwichs}";
            }
        }

        public class CategoryConverter<T> : TypeConverter
        {
            public override bool GetPropertiesSupported(ITypeDescriptorContext? context) => true;

            public override PropertyDescriptorCollection GetProperties(ITypeDescriptorContext? context, object value, Attribute[]? attributes) => TypeDescriptor.GetProperties(typeof(T));

            public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) => destinationType != typeof(string) && base.CanConvertTo(context, destinationType);
        }
    }
}