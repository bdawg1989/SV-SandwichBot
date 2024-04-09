using System.ComponentModel;

namespace SysBot.Pokemon
{
    public enum DTFormat
    {
        MMDDYY,
        DDMMYY,
        YYMMDD,
    }

    public enum TeraCrystalType : int
    {
        Base = 0,
        Black = 1,
        Distribution = 2,
        Might = 3,
    }

    public enum LobbyMethodOptions
    {
        SkipSandwich,
        OpenLobby,
        ContinueSandwich
    }

    public enum SandwichAction
    {
        AFK,
        MashA
    }

    public enum GameProgress : byte
    {
        Beginning = 0,
        UnlockedTeraSandwichs = 1,
        Unlocked3Stars = 2,
        Unlocked4Stars = 3,
        Unlocked5Stars = 4,
        Unlocked6Stars = 5,
        None = 6,
    }

    public enum EmbedColorOption
    {
        Blue,
        Green,
        Red,
        Gold,
        Purple,
        Teal,
        Orange,
        Magenta,
        LightGrey,
        DarkGrey
    }

    public enum ThumbnailOption
    {
        Gengar,
        Pikachu,
        Umbreon,
        Sylveon,
        Charmander,
        Jigglypuff,
        Flareon,
        Custom
    }

    public enum Action1Type
    {
        GoAllOut,
        HangTough,
        HealUp,
        Move1,
        Move2,
        Move3,
        Move4
    }

    public enum ScreenshotTimingOptions
    {
        [Description("1500 milliseconds")]
        _1500 = 1500, // Team SS

        [Description("9000 milliseconds")]
        _9000 = 9000 // Everything SS
    }
}