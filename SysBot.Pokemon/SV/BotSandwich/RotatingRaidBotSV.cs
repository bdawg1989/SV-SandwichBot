using Discord;
using PKHeX.Core;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Base.SwitchButton;
using static SysBot.Pokemon.RotatingSandwichSettingsSV;
using static SysBot.Pokemon.SV.BotSandwich.Blocks;

namespace SysBot.Pokemon.SV.BotSandwich
{
    public class RotatingSandwichBotSV : PokeRoutineExecutor9SV
    {
        private readonly PokeSandwichHub<PK9> Hub;
        private readonly RotatingSandwichSettingsSV Settings;
        private RemoteControlAccessList SandwicherBanList => Settings.SandwicherBanList;

        public RotatingSandwichBotSV(PokeBotState cfg, PokeSandwichHub<PK9> hub) : base(cfg)
        {
            Hub = hub;
            Settings = hub.Config.RotatingSandwichSV;
        }

        public class PlayerInfo
        {
            public string OT { get; set; }
            public int SandwichCount { get; set; }
        }

        private int LobbyError;
        private int SandwichCount;
        public static bool? currentSpawnsEnabled;
        public static int RotationCount { get; set; }
        private ulong OverworldOffset;
        private ulong ConnectedOffset;
        private readonly ulong[] TeraNIDOffsets = new ulong[3];
        private string TeraSandwichCode { get; set; } = string.Empty;
        private string BaseDescription = string.Empty;
        private readonly Dictionary<ulong, int> SandwichTracker = [];
        private SAV9SV HostSAV = new();
        private DateTime StartTime = DateTime.Now;
        private DateTime TimeForRollBackCheck = DateTime.Now;
        private int LostSandwich;
        private int EmptySandwich;

        public override async Task MainLoop(CancellationToken token)
        {
            try
            {
                Log("Identifying trainer data of the host console.");
                HostSAV = await IdentifyTrainer(token).ConfigureAwait(false);
                await InitializeHardware(Settings, token).ConfigureAwait(false);
                Log("Starting main RotatingSandwichBot loop.");
                await InnerLoop(token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log(e.Message);
            }
            Log($"Ending {nameof(RotatingSandwichBotSV)} loop.");
            await HardStop().ConfigureAwait(false);
        }

        private async Task InnerLoop(CancellationToken token)
        {
            bool partyReady;
            StartTime = DateTime.Now;
            RotationCount = 0;
            var SandwichsHosted = 0;

            while (!token.IsCancellationRequested)
            {
                // Initialize offsets at the start of the routine and cache them.
                await InitializeSessionOffsets(token).ConfigureAwait(false);

                Log($"Preparing parameter for {Settings.ActiveSandwichs[RotationCount].Title}");

                if (LobbyError >= 2)
                {
                    if (LobbyError >= 2)
                    {
                        string? msg = $"Failed to create a lobby {LobbyError} times.\n";
                        Log(msg);
                        await CloseGame(Hub.Config, token).ConfigureAwait(false);
                        await StartGameSandwich(Hub.Config, token).ConfigureAwait(false);
                        LobbyError = 0;
                        continue;
                    }
                }

                // Clear NIDs.
                await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);

                // Connect online and enter den.
                if (!await PrepareForSandwich(token).ConfigureAwait(false))
                {
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    continue;
                }

                // Wait until we're in lobby.
                if (!await GetLobbyReady(false, token).ConfigureAwait(false))
                    continue;

                if (Settings.ActiveSandwichs[RotationCount].AddedByRACommand)
                {
                    var user = Settings.ActiveSandwichs[RotationCount].User;
                    var mentionedUsers = Settings.ActiveSandwichs[RotationCount].MentionedUsers;
                    try
                    {
                            // Only get and send the Sandwich code if it's not a "Free For All"
                            var code = await GetSandwichCode(token).ConfigureAwait(false);
                            if (user != null)
                            {
                                await user.SendMessageAsync($"Your Sandwich Code is **{code}**").ConfigureAwait(false);
                            }
                            foreach (var mentionedUser in mentionedUsers)
                            {
                                await mentionedUser.SendMessageAsync($"The Sandwich Code for the private Sandwich you were invited to by {user?.Username ?? "the host"} is **{code}**").ConfigureAwait(false);
                            }
                        }
                        catch (Discord.Net.HttpException ex)
                        {
                            // Handle exception (e.g., log the error or send a message to a logging channel)
                            Log($"Failed to send DM to the user or mentioned users. They might have DMs turned off. Exception: {ex.Message}");
                        }
                }

                // Read trainers until someone joins.
                (partyReady, _) = await ReadTrainers(token).ConfigureAwait(false);
                if (!partyReady)
                {
                    // Should add overworld recovery with a game restart fallback.
                    await RegroupFromBannedUser(token).ConfigureAwait(false);

                    if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                    {
                        Log("Something went wrong, attempting to recover.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        continue;
                    }

                    // Clear trainer OTs.
                    Log("Clearing stored OTs");
                    for (int i = 0; i < 3; i++)
                    {
                        List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                        ptr[2] += i * 0x30;
                        await SwitchConnection.PointerPoke(new byte[16], ptr, token).ConfigureAwait(false);
                    }
                    continue;
                }
                await CompleteSandwich(token).ConfigureAwait(false);
                SandwichsHosted++;
                if (SandwichsHosted == Settings.SandwichSettings.TotalSandwichsToHost && Settings.SandwichSettings.TotalSandwichsToHost > 0)
                    break;
            }
            if (Settings.SandwichSettings.TotalSandwichsToHost > 0 && SandwichsHosted != 0)
                Log("Total Sandwichs to host has been met.");
        }

        public override async Task HardStop()
        {
            try
            {
                Directory.Delete("cache", true);
            }
            catch (Exception)
            {
            }

            // Remove all Mystery Shiny Sandwichs and other Sandwichs added by RA command
            Settings.ActiveSandwichs.RemoveAll(p => p.AddedByRACommand);
            Settings.ActiveSandwichs.RemoveAll(p => p.Title == "Mystery Shiny Sandwich");
            await CleanExit(CancellationToken.None).ConfigureAwait(false);
        }

        private async Task CompleteSandwich(CancellationToken token)
        {
            var trainers = new List<(ulong, SandwichMyStatus)>();

            // Ensure connection to lobby and log status
            if (!await CheckIfConnectedToLobbyAndLog(token))
            {
                await ReOpenGame(Hub.Config, token);
                return;
            }

            // Ensure in Sandwich
            if (!await EnsureInSandwich(token))
            {
                await ReOpenGame(Hub.Config, token);
                return;
            }

            // Use the ScreenshotTiming setting for the delay before taking a screenshot in Sandwich
            var screenshotDelay = (int)Settings.EmbedToggles.ScreenshotTiming;

            // Use the delay in milliseconds as needed
            await Task.Delay(screenshotDelay, token).ConfigureAwait(false);

            var lobbyTrainersFinal = new List<(ulong, SandwichMyStatus)>();
            if (!await UpdateLobbyTrainersFinal(lobbyTrainersFinal, trainers, token))
            {
                await ReOpenGame(Hub.Config, token);
                return;
            }

            // Handle duplicates and embeds first
            if (!await HandleDuplicatesAndEmbeds(lobbyTrainersFinal, token))
            {
                await ReOpenGame(Hub.Config, token);
                return;
            }

            // Delay to start ProcessBattleActions
            await Task.Delay(10_000, token).ConfigureAwait(false);

            // Process battle actions
            if (!await ProcessBattleActions(token))
            {
                await ReOpenGame(Hub.Config, token);
                return;
            }

            // Handle end of Sandwich actions
            bool ready = await HandleEndOfSandwichActions(token);
            if (!ready)
            {
                await ReOpenGame(Hub.Config, token);
                return;
            }

            // Finalize Sandwich completion
            await FinalizeSandwichCompletion(trainers, ready, token);
        }

        private async Task<bool> CheckIfConnectedToLobbyAndLog(CancellationToken token)
        {
            try
            {
                if (await IsConnectedToLobby(token).ConfigureAwait(false))
                {
                    Log("Preparing for battle!");
                    return true;
                }
                else
                {
                    Log("Not connected to lobby, reopening game.");
                    await ReOpenGame(Hub.Config, token);
                    return false;
                }
            }
            catch (Exception ex) // Catch the appropriate exception
            {
                Log($"Error checking lobby connection: {ex.Message}, reopening game.");
                await ReOpenGame(Hub.Config, token);
                return false;
            }
        }

        private async Task<bool> EnsureInSandwich(CancellationToken linkedToken)
        {
            var startTime = DateTime.Now;

            while (!await IsInSandwich(linkedToken).ConfigureAwait(false))
            {
                if (linkedToken.IsCancellationRequested || (DateTime.Now - startTime).TotalMinutes > 5)
                {
                    Log("Timeout reached or cancellation requested, reopening game.");
                    await ReOpenGame(Hub.Config, linkedToken);
                    return false;
                }

                if (!await IsConnectedToLobby(linkedToken).ConfigureAwait(false))
                {
                    Log("Lost connection to lobby, reopening game.");
                    await ReOpenGame(Hub.Config, linkedToken);
                    return false;
                }

                await Click(A, 1_000, linkedToken).ConfigureAwait(false);
            }
            return true;
        }

        public async Task<bool> UpdateLobbyTrainersFinal(List<(ulong, SandwichMyStatus)> lobbyTrainersFinal, List<(ulong, SandwichMyStatus)> trainers, CancellationToken token)
        {
            // Clear NIDs to refresh player check.
            await SwitchConnection.WriteBytesAbsoluteAsync(new byte[32], TeraNIDOffsets[0], token).ConfigureAwait(false);
            await Task.Delay(5_000, token).ConfigureAwait(false);

            // Loop through trainers again in case someone disconnected.
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);

                    if (nid == 0)
                        continue;

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    if (string.IsNullOrWhiteSpace(trainer.OT) || HostSAV.OT == trainer.OT)
                        continue;

                    lobbyTrainersFinal.Add((nid, trainer));
                }
                catch (IndexOutOfRangeException ex)
                {
                    Log($"Index out of range exception caught: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Log($"An unknown error occurred: {ex.Message}");
                    return false;
                }
            }
            return true;
        }

        private async Task<bool> HandleDuplicatesAndEmbeds(List<(ulong, SandwichMyStatus)> lobbyTrainersFinal, CancellationToken token)
        {
            var nidDupe = lobbyTrainersFinal.Select(x => x.Item1).ToList();
            var dupe = lobbyTrainersFinal.Count > 1 && nidDupe.Distinct().Count() == 1;
            if (dupe)
            {
                // We read bad data, reset game to end early and recover.
                var msg = "Oops! Something went wrong, resetting to recover.";
                bool success = false;
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    try
                    {
                        await Task.Delay(20_000, token).ConfigureAwait(false);
                        await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);
                        success = true;
                        break;
                    }
                    catch (Exception ex)
                    {
                        Log($"Attempt {attempt} failed with error: {ex.Message}");
                        if (attempt == 3)
                        {
                            Log("All attempts failed. Continuing without sending embed.");
                        }
                    }
                }

                if (!success)
                {
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    return false;
                }
            }

            var names = lobbyTrainersFinal.Select(x => x.Item2.OT).ToList();
            bool hatTrick = lobbyTrainersFinal.Count == 3 && names.Distinct().Count() == 1;

            bool embedSuccess = false;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    await EnqueueEmbed(names, "", hatTrick, false, false, true, token).ConfigureAwait(false);
                    embedSuccess = true;
                    break;
                }
                catch (Exception ex)
                {
                    Log($"Attempt {attempt} failed with error: {ex.Message}");
                    if (attempt == 3)
                    {
                        Log("All attempts failed. Continuing without sending embed.");
                    }
                }
            }

            return embedSuccess;
        }

        private async Task<bool> ProcessBattleActions(CancellationToken token)
        {
            int nextUpdateMinute = 2;
            DateTime battleStartTime = DateTime.Now;
            bool hasPerformedAction1 = false;
            bool timedOut = false;
            bool hasPressedHome = false;

            while (await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                // New check: Are we still in a Sandwich?
                if (!await IsInSandwich(token).ConfigureAwait(false))
                {
                    Log("Not in Sandwich anymore, stopping battle actions.");
                    return false;
                }
                TimeSpan timeInBattle = DateTime.Now - battleStartTime;

                // Check for battle timeout
                if (timeInBattle.TotalMinutes >= 15)
                {
                    Log("Battle timed out after 15 minutes. Even Netflix asked if I was still watching...");
                    timedOut = true;
                    break;
                }

                // Handle the first action with a delay
                if (!hasPerformedAction1)
                {
                    int action1DelayInSeconds = Settings.ActiveSandwichs[RotationCount].Action1Delay;
                    var action1Name = Settings.ActiveSandwichs[RotationCount].Action1;
                    int action1DelayInMilliseconds = action1DelayInSeconds * 1000;
                    Log($"Waiting {action1DelayInSeconds} seconds. No rush, we're chilling.");
                    await Task.Delay(action1DelayInMilliseconds, token).ConfigureAwait(false);
                    await MyActionMethod(token).ConfigureAwait(false);
                    Log($"{action1Name} done. Wasn't that fun?");
                    hasPerformedAction1 = true;
                }
                else
                {
                    // Execute Sandwich actions based on configuration
                    switch (Settings.LobbyOptions.Action)
                    {
                        case SandwichAction.AFK:
                            await Task.Delay(3_000, token).ConfigureAwait(false);
                            break;

                        case SandwichAction.MashA:
                            if (await IsConnectedToLobby(token).ConfigureAwait(false))
                            {
                                int mashADelayInMilliseconds = (int)(Settings.LobbyOptions.MashADelay * 1000);
                                await Click(A, mashADelayInMilliseconds, token).ConfigureAwait(false);
                            }
                            break;
                    }
                }

                // Periodic battle status log at 2-minute intervals
                if (timeInBattle.TotalMinutes >= nextUpdateMinute)
                {
                    Log($"{nextUpdateMinute} minutes have passed. We are still in battle...");
                    nextUpdateMinute += 2; // Update the time for the next status update.
                }
                // Check if the battle has been ongoing for 6 minutes
                if (timeInBattle.TotalMinutes >= 6 && !hasPressedHome)
                {
                    // Hit Home button twice in case we are stuck
                    await Click(HOME, 0_500, token).ConfigureAwait(false);
                    await Click(HOME, 0_500, token).ConfigureAwait(false);
                    hasPressedHome = true;
                }
                // Make sure to wait some time before the next iteration to prevent a tight loop
                await Task.Delay(1000, token); // Wait for a second before checking again
            }

            return !timedOut;
        }

        private async Task<bool> HandleEndOfSandwichActions(CancellationToken token)
        {
            Log("Sandwich lobby disbanded!");
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(DDOWN, 0_500, token).ConfigureAwait(false);
            await Click(A, 0_500, token).ConfigureAwait(false);
            bool ready = true;

            if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipSandwich)
            {
                Log($"Lost/Empty Lobbies: {LostSandwich}/{Settings.LobbyOptions.SkipSandwichLimit}");

                if (LostSandwich >= Settings.LobbyOptions.SkipSandwichLimit)
                {
                    Log($"We had {Settings.LobbyOptions.SkipSandwichLimit} lost/empty Sandwichs.. Moving on!");
                    await SanitizeRotationCount(token).ConfigureAwait(false);
                    await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
                    ready = true;
                }
            }

            return ready;
        }

        private async Task FinalizeSandwichCompletion(List<(ulong, SandwichMyStatus)> trainers, bool ready, CancellationToken token)
        {
            Log("Returning to overworld...");
            await Task.Delay(2_500, token).ConfigureAwait(false);
            while (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                await Click(A, 1_000, token).ConfigureAwait(false);

            // Update RotationCount after locating seed index
            if (Settings.ActiveSandwichs.Count > 1)
            {
                await SanitizeRotationCount(token).ConfigureAwait(false);
            }
            await EnqueueEmbed(null, "", false, false, true, false, token).ConfigureAwait(false);
            await Task.Delay(0_500, token).ConfigureAwait(false);
            await CloseGame(Hub.Config, token).ConfigureAwait(false);

            if (ready)
                await StartGameSandwich(Hub.Config, token).ConfigureAwait(false);
            else
            {
                if (Settings.ActiveSandwichs.Count > 1)
                {
                    RotationCount = (RotationCount + 1) % Settings.ActiveSandwichs.Count;
                    if (RotationCount == 0)
                    {
                        Log($"Resetting Rotation Count to {RotationCount}");
                    }

                    Log($"Moving on to next rotation for {Settings.ActiveSandwichs[RotationCount].Title}.");
                    await StartGameSandwich(Hub.Config, token).ConfigureAwait(false);
                }
                else
                    await StartGame(Hub.Config, token).ConfigureAwait(false);
            }
        }

        public async Task MyActionMethod(CancellationToken token)
        {
            // Let's rock 'n roll with these moves!
            switch (Settings.ActiveSandwichs[RotationCount].Action1)
            {
                case Action1Type.GoAllOut:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.HangTough:
                case Action1Type.HealUp:
                    await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int ddownTimes = Settings.ActiveSandwichs[RotationCount].Action1 == Action1Type.HangTough ? 1 : 2;
                    for (int i = 0; i < ddownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move1:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                case Action1Type.Move2:
                case Action1Type.Move3:
                case Action1Type.Move4:
                    await Click(A, 0_500, token).ConfigureAwait(false);
                    int moveDdownTimes = Settings.ActiveSandwichs[RotationCount].Action1 == Action1Type.Move2 ? 1 : Settings.ActiveSandwichs[RotationCount].Action1 == Action1Type.Move3 ? 2 : 3;
                    for (int i = 0; i < moveDdownTimes; i++)
                    {
                        await Click(DDOWN, 0_500, token).ConfigureAwait(false);
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        await Click(A, 0_500, token).ConfigureAwait(false);
                    }
                    break;

                default:
                    Console.WriteLine("Unknown action, what's the move?");
                    throw new InvalidOperationException("Unknown action type!");
            }
        }

        private async Task SanitizeRotationCount(CancellationToken token)
        {
            try
            {
                await Task.Delay(50, token).ConfigureAwait(false);

                if (Settings.ActiveSandwichs.Count == 0)
                {
                    Log("ActiveSandwichs is empty. Exiting SanitizeRotationCount.");
                    RotationCount = 0;
                    return;
                }

                // Normalize RotationCount to be within the range of ActiveSandwichs
                RotationCount = Math.Max(0, Math.Min(RotationCount, Settings.ActiveSandwichs.Count - 1));

                // Update SandwichUpNext for the next Sandwich
                int nextSandwichIndex = FindNextPrioritySandwichIndex(RotationCount, Settings.ActiveSandwichs);
                for (int i = 0; i < Settings.ActiveSandwichs.Count; i++)
                {
                    Settings.ActiveSandwichs[i].SandwichUpNext = i == nextSandwichIndex;
                }

                // Process RA command Sandwichs
                if (Settings.ActiveSandwichs[RotationCount].AddedByRACommand)
                {
                    bool isMysterySandwich = Settings.ActiveSandwichs[RotationCount].Title.Contains("Mystery Shiny Sandwich");
                    bool isUserRequestedSandwich = !isMysterySandwich && Settings.ActiveSandwichs[RotationCount].Title.Contains("'s Requested Sandwich");

                    if (isUserRequestedSandwich || isMysterySandwich)
                    {
                        Log($"Sandwich for {Settings.ActiveSandwichs[RotationCount].Title} was added via RA command and will be removed from the rotation list.");
                        Settings.ActiveSandwichs.RemoveAt(RotationCount);
                        // Adjust RotationCount after removal
                        if (RotationCount >= Settings.ActiveSandwichs.Count)
                        {
                            RotationCount = 0;
                        }

                        // After a Sandwich is removed, find the new next priority Sandwich and update SandwichUpNext
                        nextSandwichIndex = FindNextPrioritySandwichIndex(RotationCount, Settings.ActiveSandwichs);
                        for (int i = 0; i < Settings.ActiveSandwichs.Count; i++)
                        {
                            Settings.ActiveSandwichs[i].SandwichUpNext = i == nextSandwichIndex;
                        }
                    }
                }

                if (Settings.SandwichSettings.RandomRotation)
                {
                    ProcessRandomRotation();
                    return;
                }

                // Find next priority Sandwich
                int nextPriorityIndex = FindNextPrioritySandwichIndex(RotationCount, Settings.ActiveSandwichs);
                if (nextPriorityIndex != -1)
                {
                    RotationCount = nextPriorityIndex;
                }
                Log($"Next Sandwich in the list: {Settings.ActiveSandwichs[RotationCount].Title}.");
            }
            catch (Exception ex)
            {
                Log($"Index was out of range. Resetting RotationCount to 0. {ex.Message}");
                RotationCount = 0;
            }
        }

        private int FindNextPrioritySandwichIndex(int currentRotationCount, List<RotatingSandwichParameters> Sandwichs)
        {
            if (Sandwichs == null || Sandwichs.Count == 0)
            {
                // Handle edge case where Sandwichs list is empty or null
                return currentRotationCount;
            }

            int count = Sandwichs.Count;

            // First, check for user-requested RA command Sandwichs
            for (int i = 0; i < count; i++)
            {
                int index = (currentRotationCount + i) % count;
                RotatingSandwichParameters Sandwich = Sandwichs[index];

                if (Sandwich.AddedByRACommand && !Sandwich.Title.Contains("Mystery Shiny Sandwich"))
                {
                    return index; // Prioritize user-requested Sandwichs
                }
            }

            // Next, check for Mystery Shiny Sandwichs if enabled
            if (Settings.SandwichSettings.MysterySandwichs)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = (currentRotationCount + i) % count;
                    RotatingSandwichParameters Sandwich = Sandwichs[index];

                    if (Sandwich.Title.Contains("Mystery Shiny Sandwich"))
                    {
                        return index; // Only consider Mystery Shiny Sandwichs after user-requested Sandwichs
                    }
                }
            }

            // Return current rotation count if no priority Sandwichs are found
            return -1;
        }
        private void ProcessRandomRotation()
        {
            // Turn off RandomRotation if both RandomRotation and MysterySandwich are true
            if (Settings.SandwichSettings.RandomRotation && Settings.SandwichSettings.MysterySandwichs)
            {
                Settings.SandwichSettings.RandomRotation = false;
                Log("RandomRotation turned off due to MysterySandwichs being active.");
                return;  // Exit the method as RandomRotation is now turned off
            }

            // Check the remaining Sandwichs for any added by the RA command
            for (var i = RotationCount; i < Settings.ActiveSandwichs.Count; i++)
            {
                if (Settings.ActiveSandwichs[i].AddedByRACommand)
                {
                    RotationCount = i;
                    Log($"Setting Rotation Count to {RotationCount}");
                    return;  // Exit method as a Sandwich added by RA command was found
                }
            }

            // If no Sandwich added by RA command was found, select a random Sandwich
            var random = new Random();
            RotationCount = random.Next(Settings.ActiveSandwichs.Count);
            Log($"Setting Rotation Count to {RotationCount}");
        }
        private async Task<bool> PrepareForSandwich(CancellationToken token)
        {
            if (Settings.ActiveSandwichs[RotationCount].AddedByRACommand)
            {
                var user = Settings.ActiveSandwichs[RotationCount].User;
                var mentionedUsers = Settings.ActiveSandwichs[RotationCount].MentionedUsers;
                    try
                    {
                        // Only send the message if it's not a "Free For All"
                        if (user != null)
                        {
                            await user.SendMessageAsync("Get Ready! Your Sandwich is being prepared now!").ConfigureAwait(false);
                        }

                        foreach (var mentionedUser in mentionedUsers)
                        {
                            await mentionedUser.SendMessageAsync($"Get Ready! The Sandwich you were invited to by {user?.Username ?? "the host"} is about to start!").ConfigureAwait(false);
                        }
                    }
                    catch (Discord.Net.HttpException ex)
                    {
                        // Handle exception (e.g., log the error or send a message to a logging channel)
                        Log($"Failed to send DM to the user or mentioned users. They might have DMs turned off. Exception: {ex.Message}");
                    }
                }

            Log("Preparing lobby...");
            LobbyFiltersCategory settings = new();

            if (!await ConnectToOnline(Hub.Config, token))
            {
                return false;
            }

            await Task.Delay(0_500, token).ConfigureAwait(false);
            await Click(HOME, 0_500, token).ConfigureAwait(false);
            await Click(HOME, 0_500, token).ConfigureAwait(false);

            for (int i = 0; i < 4; i++)
                await Click(B, 1_000, token).ConfigureAwait(false);

            await Task.Delay(1_500, token).ConfigureAwait(false);

            if (!await IsOnOverworld(OverworldOffset, token).ConfigureAwait(false))
                return false;

            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);

                await Click(A, 3_000, token).ConfigureAwait(false);

            await Click(A, 8_000, token).ConfigureAwait(false);
            return true;
        }

        private async Task<bool> GetLobbyReady(bool recovery, CancellationToken token)
        {
            var x = 0;
            Log("Connecting to lobby...");
            while (!await IsConnectedToLobby(token).ConfigureAwait(false))
            {
                await Click(A, 1_000, token).ConfigureAwait(false);
                x++;
                if (x == 15 && recovery)
                {
                    Log("No den here! Rolling again.");
                    return false;
                }
                if (x == 45)
                {
                    Log("Failed to connect to lobby, restarting game incase we were in battle/bad connection.");
                    LobbyError++;
                    await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                    Log("Attempting to restart routine!");
                    return false;
                }
            }
            return true;
        }

        private async Task<string> GetSandwichCode(CancellationToken token)
        {
            var data = await SwitchConnection.PointerPeek(6, Offsets.TeraSandwichCodePointer, token).ConfigureAwait(false);
            TeraSandwichCode = Encoding.ASCII.GetString(data).ToLower(); // Convert to lowercase for easier reading
            return $"{TeraSandwichCode}";
        }

        private async Task<bool> CheckIfTrainerBanned(SandwichMyStatus trainer, ulong nid, int player, CancellationToken token)
        {
            SandwichTracker.TryAdd(nid, 0);
            var msg = string.Empty;
            var banResultCFW = SandwicherBanList.List.FirstOrDefault(x => x.ID == nid);
            bool isBanned = banResultCFW != default;

            if (isBanned)
            {
                msg = $"{banResultCFW!.Name} was found in the host's ban list.\n{banResultCFW.Comment}";
                Log(msg);
                await EnqueueEmbed(null, msg, false, true, false, false, token).ConfigureAwait(false);
                return true;
            }
            return false;
        }

        private async Task<(bool, List<(ulong, SandwichMyStatus)>)> ReadTrainers(CancellationToken token)
        {
            if (!await IsConnectedToLobby(token))
                return (false, new List<(ulong, SandwichMyStatus)>());

            await EnqueueEmbed(null, "", false, false, false, false, token).ConfigureAwait(false);

            List<(ulong, SandwichMyStatus)> lobbyTrainers = [];
            var wait = TimeSpan.FromSeconds(Settings.SandwichSettings.TimeToWait);
            var endTime = DateTime.Now + wait;
            bool full = false;

            while (!full && DateTime.Now < endTime)
            {
                if (!await IsConnectedToLobby(token))
                    return (false, lobbyTrainers);

                for (int i = 0; i < 3; i++)
                {
                    var player = i + 2;
                    Log($"Waiting for Player {player} to load...");

                    // Check connection to lobby here
                    if (!await IsConnectedToLobby(token))
                        return (false, lobbyTrainers);

                    var nidOfs = TeraNIDOffsets[i];
                    var data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                    var nid = BitConverter.ToUInt64(data, 0);
                    while (nid == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);

                        // Check connection to lobby again here after the delay
                        if (!await IsConnectedToLobby(token))
                            return (false, lobbyTrainers);

                        data = await SwitchConnection.ReadBytesAbsoluteAsync(nidOfs, 8, token).ConfigureAwait(false);
                        nid = BitConverter.ToUInt64(data, 0);
                    }

                    List<long> ptr = new(Offsets.Trader2MyStatusPointer);
                    ptr[2] += i * 0x30;
                    var trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);

                    while (trainer.OT.Length == 0 && DateTime.Now < endTime)
                    {
                        await Task.Delay(0_500, token).ConfigureAwait(false);

                        // Check connection to lobby again here after the delay
                        if (!await IsConnectedToLobby(token))
                            return (false, lobbyTrainers);

                        trainer = await GetTradePartnerMyStatus(ptr, token).ConfigureAwait(false);
                    }

                    if (nid != 0 && !string.IsNullOrWhiteSpace(trainer.OT))
                    {
                        if (await CheckIfTrainerBanned(trainer, nid, player, token).ConfigureAwait(false))
                            return (false, lobbyTrainers);
                    }

                    // Check if the NID is already in the list to prevent duplicates
                    if (lobbyTrainers.Any(x => x.Item1 == nid))
                    {
                        Log($"Duplicate NID detected: {nid}. Skipping...");
                        continue; // Skip adding this NID if it's a duplicate
                    }

                    // If NID is not a duplicate and has a valid trainer OT, add to the list
                    if (nid > 0 && trainer.OT.Length > 0)
                        lobbyTrainers.Add((nid, trainer));

                    full = lobbyTrainers.Count == 3;
                    if (full || DateTime.Now >= endTime)
                        break;
                }
            }

            await Task.Delay(5_000, token).ConfigureAwait(false);

            if (lobbyTrainers.Count == 0)
            {
                EmptySandwich++;
                LostSandwich++;
                Log($"Nobody joined the Sandwich, recovering...");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                    Log($"Empty Sandwich Count #{EmptySandwich}");
                if (Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.SkipSandwich)
                    Log($"Lost/Empty Lobbies: {LostSandwich}/{Settings.LobbyOptions.SkipSandwichLimit}");

                return (false, lobbyTrainers);
            }

            SandwichCount++; // Increment SandwichCount only when a Sandwich is actually starting.
            Log($"Sandwich #{SandwichCount} is starting!");
            if (EmptySandwich != 0)
                EmptySandwich = 0;
            return (true, lobbyTrainers);
        }

        private async Task<bool> IsConnectedToLobby(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.TeraLobbyIsConnected, 1, token).ConfigureAwait(false);
            return data[0] != 0x00; // 0 when in lobby but not connected
        }

        private async Task<bool> IsInSandwich(CancellationToken token)
        {
            var data = await SwitchConnection.ReadBytesMainAsync(Offsets.LoadedIntoDesiredState, 1, token).ConfigureAwait(false);
            return data[0] == 0x02; // 2 when in Sandwich, 1 when not
        }

        private async Task RegroupFromBannedUser(CancellationToken token)
        {
            Log("Attempting to remake lobby..");
            await Click(B, 2_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(A, 3_000, token).ConfigureAwait(false);
            await Click(B, 1_000, token).ConfigureAwait(false);
        }

        private async Task InitializeSessionOffsets(CancellationToken token)
        {
            Log("Caching session offsets...");
            OverworldOffset = await SwitchConnection.PointerAll(Offsets.OverworldPointer, token).ConfigureAwait(false);
            ConnectedOffset = await SwitchConnection.PointerAll(Offsets.IsConnectedPointer, token).ConfigureAwait(false);
            var nidPointer = new long[] { Offsets.LinkTradePartnerNIDPointer[0], Offsets.LinkTradePartnerNIDPointer[1], Offsets.LinkTradePartnerNIDPointer[2] };
            for (int p = 0; p < TeraNIDOffsets.Length; p++)
            {
                nidPointer[2] = Offsets.LinkTradePartnerNIDPointer[2] + p * 0x8;
                TeraNIDOffsets[p] = await SwitchConnection.PointerAll(nidPointer, token).ConfigureAwait(false);
            }
            Log("Caching offsets complete!");
        }

        private static async Task<bool> IsValidImageUrlAsync(string url)
        {
            using var httpClient = new HttpClient();
            try
            {
                var response = await httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch (HttpRequestException ex) when (ex.InnerException is WebException webEx && webEx.Status == WebExceptionStatus.TrustFailure)
            {
            }
            catch (Exception)
            {
            }
            return false;
        }

        private static readonly char[] separator = [','];
        private static readonly char[] separatorArray = ['-'];

        private async Task EnqueueEmbed(List<string>? names, string message, bool hatTrick, bool disband, bool upnext, bool Sandwichstart, CancellationToken token)
        {
            string code = string.Empty;

            // Apply delay only if the Sandwich was added by RA command, not a Mystery Shiny Sandwich, and has a code
            if (Settings.ActiveSandwichs[RotationCount].AddedByRACommand &&
                Settings.ActiveSandwichs[RotationCount].Title != "Mystery Shiny Sandwich" &&
                code != "Free For All")
            {
                await Task.Delay(Settings.EmbedToggles.RequestEmbedTime * 1000, token).ConfigureAwait(false);
            }

            // Description can only be up to 4096 characters.
            //var description = Settings.ActiveSandwichs[RotationCount].Description.Length > 0 ? string.Join("\n", Settings.ActiveSandwichs[RotationCount].Description) : "";
            var description = Settings.EmbedToggles.SandwichEmbedDescription.Length > 0 ? string.Join("\n", Settings.EmbedToggles.SandwichEmbedDescription) : "";
            if (description.Length > 4096) description = description[..4096];

            if (EmptySandwich == Settings.LobbyOptions.EmptySandwichLimit && Settings.LobbyOptions.LobbyMethod == LobbyMethodOptions.OpenLobby)
                EmptySandwich = 0;

            if (disband) // Wait for trainer to load before disband
                await Task.Delay(5_000, token).ConfigureAwait(false);

            byte[]? bytes = [];
            if (Settings.EmbedToggles.TakeScreenshot && !upnext)
                try
                {
                    bytes = await SwitchConnection.PixelPeek(token).ConfigureAwait(false) ?? [];
                }
                catch (Exception ex)
                {
                    Log($"Error while fetching pixels: {ex.Message}");
                }

            string disclaimer = Settings.ActiveSandwichs.Count > 1
                                ? $"notpaldea.net"
                                : "";

            var turl = string.Empty;
            var form = string.Empty;

            Log($"Rotation Count: {RotationCount} | Species is {Settings.ActiveSandwichs[RotationCount].Title}");
            if (!disband && !upnext && !Sandwichstart)
                Log($"Sandwich Code is: {code}");

            // Use the dominant color, unless it's a disband or hatTrick situation
            var embedColor = disband ? Color.Red : hatTrick ? Color.Purple : Color.Blue;

            TimeSpan duration = new(0, 2, 31);

            // Calculate the future time by adding the duration to the current time
            DateTimeOffset futureTime = DateTimeOffset.Now.Add(duration);

            // Convert the future time to Unix timestamp
            long futureUnixTime = futureTime.ToUnixTimeSeconds();

            // Create the future time message using Discord's timestamp formatting
            string futureTimeMessage = $"**Sandwich Posting: <t:{futureUnixTime}:R>**";

            // Initialize the EmbedBuilder object
            var embed = new EmbedBuilder()
            {
                Title = disband ? $"**Sandwich canceled: [{TeraSandwichCode}]**" : upnext && Settings.SandwichSettings.TotalSandwichsToHost != 0 ? $"Sandwich Ended - Preparing Next Sandwich!" : upnext && Settings.SandwichSettings.TotalSandwichsToHost == 0 ? $"Sandwich Ended - Preparing Next Sandwich!" : "",
                Color = embedColor,
                Description = disband ? message : upnext ? Settings.SandwichSettings.TotalSandwichsToHost == 0 ? $"# {Settings.ActiveSandwichs[RotationCount].Title}\n\n{futureTimeMessage}" : $"# {Settings.ActiveSandwichs[RotationCount].Title}\n\n{futureTimeMessage}" : Sandwichstart ? "" : description,
                ImageUrl = bytes.Length > 0 ? "attachment://zap.jpg" : default,
            };

            // Only include footer if not posting 'upnext' embed with the 'Preparing Sandwich' title
            if (!(upnext && Settings.SandwichSettings.TotalSandwichsToHost == 0))
            {
                string programIconUrl = $"https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/icon4.png";
                int SandwichsInRotationCount = Hub.Config.RotatingSandwichSV.ActiveSandwichs.Count(r => !r.AddedByRACommand);
                // Calculate uptime
                TimeSpan uptime = DateTime.Now - StartTime;

                // Check for singular or plural days/hours
                string dayLabel = uptime.Days == 1 ? "day" : "days";
                string hourLabel = uptime.Hours == 1 ? "hour" : "hours";
                string minuteLabel = uptime.Minutes == 1 ? "minute" : "minutes";

                // Format the uptime string, omitting the part if the value is 0
                string uptimeFormatted = "";
                if (uptime.Days > 0)
                {
                    uptimeFormatted += $"{uptime.Days} {dayLabel} ";
                }
                if (uptime.Hours > 0 || uptime.Days > 0) // Show hours if there are any hours, or if there are days even if hours are 0
                {
                    uptimeFormatted += $"{uptime.Hours} {hourLabel} ";
                }
                if (uptime.Minutes > 0 || uptime.Hours > 0 || uptime.Days > 0) // Show minutes if there are any minutes, or if there are hours/days even if minutes are 0
                {
                    uptimeFormatted += $"{uptime.Minutes} {minuteLabel}";
                }

                // Trim any excess whitespace from the string
                uptimeFormatted = uptimeFormatted.Trim();
                embed.WithFooter(new EmbedFooterBuilder()
                {
                    Text = $"Completed Sandwichs: {SandwichCount}\nActiveSandwichs: {SandwichsInRotationCount} | Uptime: {uptimeFormatted}\n" + disclaimer,
                    IconUrl = programIconUrl
                });
            }

            // Only include author (header) if not posting 'upnext' embed with the 'Preparing Sandwich' title
            if (!(upnext && Settings.SandwichSettings.TotalSandwichsToHost == 0))
            {
                // Set the author (header) of the embed with the tera icon
                embed.WithAuthor(new EmbedAuthorBuilder()
                {
                    Name = Settings.ActiveSandwichs[RotationCount].Title,
                });
            }
            if (!disband && !upnext && !Sandwichstart)
            {
                StringBuilder statsField = new();
            }
            if (!disband && names is null && !upnext)
            {
                embed.AddField(Settings.EmbedToggles.IncludeCountdown ? $"**__Sandwich Starting__**:\n**<t:{DateTimeOffset.Now.ToUnixTimeSeconds() + Settings.SandwichSettings.TimeToWait}:R>**" : $"**Waiting in lobby!**", $"Sandwich Code: **{code}**", true);
            }
            if (!disband && names is not null && !upnext)
            {
                var players = string.Empty;
                if (names.Count == 0)
                    players = "Our party dipped on us :/";
                else
                {
                    int i = 2;
                    names.ForEach(x =>
                    {
                        players += $"Player {i} - **{x}**\n";
                        i++;
                    });
                }

                embed.AddField($"**Sandwich #{SandwichCount} is starting!**", players);
            }
            var fileName = $"Sandwichecho{RotationCount}.jpg";
            embed.ThumbnailUrl = turl;
            embed.WithImageUrl($"attachment://{fileName}");
            EchoUtil.SandwichEmbed(bytes, fileName, embed);
        }

        private async Task<bool> ConnectToOnline(PokeSandwichHubConfig config, CancellationToken token)
        {
            int attemptCount = 0;
            const int maxAttempt = 5;
            const int waitTime = 10; // time in minutes to wait after max attempts

            while (true) // Loop until a successful connection is made or the task is canceled
            {
                if (token.IsCancellationRequested)
                {
                    Log("Connection attempt canceled.");
                    break;
                }
                try
                {
                    if (await IsConnectedOnline(ConnectedOffset, token).ConfigureAwait(false))
                    {
                        Log("Connection established successfully.");
                        break; // Exit the loop if connected successfully
                    }

                    if (attemptCount >= maxAttempt)
                    {
                        Log($"Failed to connect after {maxAttempt} attempts. Assuming a softban. Initiating wait for {waitTime} minutes before retrying.");
                        // Log details about sending an embed message
                        Log("Sending an embed message to notify about technical difficulties.");
                        EmbedBuilder embed = new()
                        {
                            Title = "Experiencing Technical Difficulties",
                            Description = "The bot is experiencing issues connecting online. Please stand by as we try to resolve the issue.",
                            Color = Color.Red,
                            ThumbnailUrl = "https://raw.githubusercontent.com/bdawg1989/sprites/main/imgs/x.png"
                        };
                        EchoUtil.SandwichEmbed(null, "", embed);
                        // Waiting process
                        await Click(B, 0_500, token).ConfigureAwait(false);
                        await Click(B, 0_500, token).ConfigureAwait(false);
                        Log($"Waiting for {waitTime} minutes before attempting to reconnect.");
                        await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                        Log("Attempting to reopen the game.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        attemptCount = 0; // Reset attempt count
                    }

                    attemptCount++;
                    Log($"Attempt {attemptCount} of {maxAttempt}: Trying to connect online...");

                    // Connection attempt logic
                    await Click(X, 3_000, token).ConfigureAwait(false);
                    await Click(L, 5_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);

                    // Wait a bit before rechecking the connection status
                    await Task.Delay(5000, token).ConfigureAwait(false); // Wait 5 seconds before rechecking

                    if (attemptCount < maxAttempt)
                    {
                        Log("Rechecking the online connection status...");
                        // Wait and recheck logic
                        await Click(B, 0_500, token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log($"Exception occurred during connection attempt: {ex.Message}");
                    // Handle exceptions, like connectivity issues here

                    if (attemptCount >= maxAttempt)
                    {
                        Log($"Failed to connect after {maxAttempt} attempts due to exception. Waiting for {waitTime} minutes before retrying.");
                        await Task.Delay(TimeSpan.FromMinutes(waitTime), token).ConfigureAwait(false);
                        Log("Attempting to reopen the game.");
                        await ReOpenGame(Hub.Config, token).ConfigureAwait(false);
                        attemptCount = 0;
                    }
                }
            }

            // Final steps after connection is established
            await Task.Delay(3_000 + config.Timings.ExtraTimeConnectOnline, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Click(B, 0_500, token).ConfigureAwait(false);
            await Task.Delay(3_000, token).ConfigureAwait(false);

            return true;
        }

        public async Task StartGameSandwich(PokeSandwichHubConfig config, CancellationToken token)
        {
            var timing = config.Timings;
            var loadPro = timing.RestartGameSettings.ProfileSelectSettings.ProfileSelectionRequired ? timing.RestartGameSettings.ProfileSelectSettings.ExtraTimeLoadProfile : 0;

            await Click(A, 1_000 + loadPro, token).ConfigureAwait(false); // Initial "A" Press to start the Game + a delay if needed for profiles to load

            // Only send extra Presses if we need to
            if (timing.RestartGameSettings.ProfileSelectSettings.ProfileSelectionRequired)
            {
                await Click(A, 1_000, token).ConfigureAwait(false); // Now we are on the Profile Screen
                await Click(A, 1_000, token).ConfigureAwait(false); // Select the profile
            }

            // Digital game copies take longer to load
            if (timing.RestartGameSettings.CheckGameDelay)
            {
                await Task.Delay(2_000 + timing.RestartGameSettings.ExtraTimeCheckGame, token).ConfigureAwait(false);
            }

            // If they have DLC on the system and can't use it, requires an UP + A to start the game.
            if (timing.RestartGameSettings.CheckForDLC)
            {
                await Click(DUP, 0_600, token).ConfigureAwait(false);
                await Click(A, 0_600, token).ConfigureAwait(false);
            }

            Log("Restarting the game!");

            await Task.Delay(19_000 + timing.RestartGameSettings.ExtraTimeLoadGame, token).ConfigureAwait(false); // Wait for the game to load before writing to memory

            if (Settings.ActiveSandwichs.Count > 1)
            {
                Log($"Rotation for {Settings.ActiveSandwichs[RotationCount].Title} has been found.");

                SandwichDataBlocks.AdjustKWildSpawnsEnabledType(Settings.SandwichSettings.DisableOverworldSpawns);

                if (Settings.SandwichSettings.DisableOverworldSpawns)
                {
                    Log("Checking current state of Overworld Spawns.");
                    if (currentSpawnsEnabled.HasValue)
                    {
                        Log($"Current Overworld Spawns state: {currentSpawnsEnabled.Value}");

                        if (currentSpawnsEnabled.Value)
                        {
                            Log("Overworld Spawns are enabled, attempting to disable.");
                            await WriteBlock(false, SandwichDataBlocks.KWildSpawnsEnabled, token, currentSpawnsEnabled);
                            currentSpawnsEnabled = false;
                            Log("Overworld Spawns successfully disabled.");
                        }
                        else
                        {
                            Log("Overworld Spawns are already disabled, no action taken.");
                        }
                    }
                }
                else // When Settings.DisableOverworldSpawns is false, ensure Overworld spawns are enabled
                {
                    Log("Settings indicate Overworld Spawns should be enabled. Checking current state.");
                    Log($"Current Overworld Spawns state: {currentSpawnsEnabled.Value}");

                    if (!currentSpawnsEnabled.Value)
                    {
                        Log("Overworld Spawns are disabled, attempting to enable.");
                        await WriteBlock(true, SandwichDataBlocks.KWildSpawnsEnabled, token, currentSpawnsEnabled);
                        currentSpawnsEnabled = true;
                        Log("Overworld Spawns successfully enabled.");
                    }
                    else
                    {
                        Log("Overworld Spawns are already enabled, no action needed.");
                    }
                }
            }

            for (int i = 0; i < 8; i++)
                await Click(A, 1_000, token).ConfigureAwait(false);

            var timer = 60_000;
            while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
            {
                await Task.Delay(1_000, token).ConfigureAwait(false);
                timer -= 1_000;
                if (timer <= 0 && !timing.RestartGameSettings.AvoidSystemUpdate)
                {
                    Log("Still not in the game, initiating rescue protocol!");
                    while (!await IsOnOverworldTitle(token).ConfigureAwait(false))
                        await Click(A, 6_000, token).ConfigureAwait(false);
                    break;
                }
            }

            await Task.Delay(5_000 + timing.ExtraTimeLoadOverworld, token).ConfigureAwait(false);
            Log("Back in the overworld!");

            LostSandwich = 0;
        }
    }
}