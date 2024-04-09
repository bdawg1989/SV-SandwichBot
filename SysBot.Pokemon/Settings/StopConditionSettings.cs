using PKHeX.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace SysBot.Pokemon;

public class StopConditionSettings
{
    private const string StopConditions = nameof(StopConditions);
    public override string ToString() => "Stop Condition Settings";

    [Category(StopConditions), Description("Stops only on Pokémon of this species. No restrictions if set to \"None\".")]
    public Species StopOnSpecies { get; set; }

    [Category(StopConditions), Description("Stops only on Pokémon with this FormID. No restrictions if left blank.")]
    public int? StopOnForm { get; set; }

    [Category(StopConditions), Description("Stop only on Pokémon of the specified nature.")]
    public Nature TargetNature { get; set; } = Nature.Random;

    [Category(StopConditions), Description("Minimum accepted IVs in the format HP/Atk/Def/SpA/SpD/Spe. Use \"x\" for unchecked IVs and \"/\" as a separator.")]
    public string TargetMinIVs { get; set; } = "";

    [Category(StopConditions), Description("Maximum accepted IVs in the format HP/Atk/Def/SpA/SpD/Spe. Use \"x\" for unchecked IVs and \"/\" as a separator.")]
    public string TargetMaxIVs { get; set; } = "";

    [Category(StopConditions), Description("Selects the shiny type to stop on.")]
    public TargetShinyType ShinyTarget { get; set; } = TargetShinyType.DisableOption;

    [Category(StopConditions), Description("Stop only on Pokémon that have a mark.")]
    public bool MarkOnly { get; set; }

    [Category(StopConditions), Description("List of marks to ignore separated by commas. Use the full name, e.g. \"Uncommon Mark, Dawn Mark, Prideful Mark\".")]
    public string UnwantedMarks { get; set; } = "";

    [Category(StopConditions), Description("Holds Capture button to record a 30 second clip when a matching Pokémon is found by EncounterBot or Fossilbot.")]
    public bool CaptureVideoClip { get; set; }

    [Category(StopConditions), Description("Extra time in milliseconds to wait after an encounter is matched before pressing Capture for EncounterBot or Fossilbot.")]
    public int ExtraTimeWaitCaptureVideo { get; set; } = 10000;

    [Category(StopConditions), Description("If set to TRUE, matches both ShinyTarget and TargetIVs settings. Otherwise, looks for either ShinyTarget or TargetIVs match.")]
    public bool MatchShinyAndIV { get; set; } = true;

    [Category(StopConditions), Description("If not empty, the provided string will be prepended to the result found log message to Echo alerts for whomever you specify. For Discord, use <@userIDnumber> to mention.")]
    public string MatchFoundEchoMention { get; set; } = string.Empty;


    public readonly string[] MarkTitle =
    {
        " the Peckish"," the Sleepy"," the Dozy"," the Early Riser"," the Cloud Watcher"," the Sodden"," the Thunderstruck"," the Snow Frolicker"," the Shivering"," the Parched"," the Sandswept"," the Mist Drifter",
        " the Chosen One"," the Catch of the Day"," the Curry Connoisseur"," the Sociable"," the Recluse"," the Rowdy"," the Spacey"," the Anxious"," the Giddy"," the Radiant"," the Serene"," the Feisty"," the Daydreamer",
        " the Joyful"," the Furious"," the Beaming"," the Teary-Eyed"," the Chipper"," the Grumpy"," the Scholar"," the Rampaging"," the Opportunist"," the Stern"," the Kindhearted"," the Easily Flustered"," the Driven",
        " the Apathetic"," the Arrogant"," the Reluctant"," the Humble"," the Pompous"," the Lively"," the Worn-Out", " of the Distant Past", " the Twinkling Star", " the Paldea Champion", " the Great", " the Teeny", " the Treasure Hunter",
        " the Reliable Partner", " the Gourmet", " the One-in-a-Million", " the Former Alpha", " the Unrivaled", " the Former Titan",
    };


    public enum TargetShinyType
    {
        DisableOption,  // Doesn't care
        NonShiny,       // Match nonshiny only
        AnyShiny,       // Match any shiny regardless of type
        StarOnly,       // Match star shiny only
        SquareOnly,     // Match square shiny only
    }
}
