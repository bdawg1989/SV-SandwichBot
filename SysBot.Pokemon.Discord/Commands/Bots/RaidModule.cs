using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using PKHeX.Core;
using SysBot.Pokemon.SV.BotSandwich.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static SysBot.Pokemon.RotatingSandwichSettingsSV;
using static SysBot.Pokemon.SV.BotSandwich.RotatingSandwichBotSV;

namespace SysBot.Pokemon.Discord.Commands.Bots
{
    [Summary("Generates and queues various silly trade additions")]
    public partial class SandwichModule<T> : ModuleBase<SocketCommandContext> where T : PKM, new()
    {
        private readonly PokeSandwichHub<T> Hub = SysCord<T>.Runner.Hub;
        private static DiscordSocketClient _client => SysCord<T>.Instance.GetClient();

        [Command("limitrequests")]
        [Alias("lr")]
        [Summary("Sets the limit on the number of requests a user can make.")]
        [RequireSudo]
        public async Task SetLimitRequestsAsync([Summary("The new limit for requests. Set to 0 to disable.")] int newLimit)
        {
            var settings = Hub.Config.RotatingSandwichSV.SandwichSettings;
            settings.LimitRequests = newLimit;

            await ReplyAsync($"LimitRequests updated to {newLimit}.").ConfigureAwait(false);
        }

        [Command("limitrequeststime")]
        [Alias("lrt")]
        [Summary("Sets the time users must wait once their request limit is reached.")]
        [RequireSudo]
        public async Task SetLimitRequestsTimeAsync([Summary("The new time in minutes. Set to 0 to disable.")] int newTime)
        {
            var settings = Hub.Config.RotatingSandwichSV.SandwichSettings;
            settings.LimitRequestsTime = newTime;

            await ReplyAsync($"LimitRequestsTime updated to {newTime} minutes.").ConfigureAwait(false);
        }

        [Command("addlimitbypass")]
        [Alias("alb")]
        [Summary("Adds a user or role to the bypass list for request limits.")]
        [RequireSudo]
        public async Task AddBypassLimitAsync([Remainder] string mention)
        {
            string type;
            string nameToAdd;
            if (MentionUtils.TryParseUser(mention, out ulong idToAdd))
            {
                var user = Context.Guild.GetUser(idToAdd);
                nameToAdd = user?.Username ?? "Unknown User";
                type = "User";
            }
            // Check if mention is a role
            else if (MentionUtils.TryParseRole(mention, out idToAdd))
            {
                var role = Context.Guild.GetRole(idToAdd);
                nameToAdd = role?.Name ?? "Unknown Role";
                type = "Role";
            }
            else
            {
                await ReplyAsync("Invalid user or role.").ConfigureAwait(false);
                return;
            }

            if (Hub.Config.RotatingSandwichSV.SandwichSettings.BypassLimitRequests.TryAdd(idToAdd, nameToAdd))
            {

                await ReplyAsync($"Added {type} '{nameToAdd}' to bypass list.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"{type} '{nameToAdd}' is already in the bypass list.").ConfigureAwait(false);
            }
        }

        [Command("repeek")]
        [Summary("Take and send a screenshot from the currently configured Switch.")]
        [RequireOwner]
        public async Task RePeek()
        {
            string ip = SandwichModule<T>.GetBotIPFromJsonConfig(); // Fetch the IP from the config
            var source = new CancellationTokenSource();
            var token = source.Token;

            var bot = SysCord<T>.Runner.GetBot(ip);
            if (bot == null)
            {
                await ReplyAsync($"No bot found with the specified IP address ({ip}).").ConfigureAwait(false);
                return;
            }

            _ = Array.Empty<byte>();
            byte[]? bytes;
            try
            {
                bytes = await bot.Bot.Connection.PixelPeek(token).ConfigureAwait(false) ?? Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                await ReplyAsync($"Error while fetching pixels: {ex.Message}");
                return;
            }

            if (bytes.Length == 0)
            {
                await ReplyAsync("No screenshot data received.");
                return;
            }

            using MemoryStream ms = new(bytes);
            var img = "cap.jpg";
            var embed = new EmbedBuilder { ImageUrl = $"attachment://{img}", Color = Color.Purple }
                .WithFooter(new EmbedFooterBuilder { Text = $"Here's your screenshot." });

            await Context.Channel.SendFileAsync(ms, img, embed: embed.Build());
        }

        private static string GetBotIPFromJsonConfig()
        {
            try
            {
                // Read the file and parse the JSON
                var jsonData = File.ReadAllText(NotSandwichBot.ConfigPath);
                var config = JObject.Parse(jsonData);

                // Access the IP address from the first bot in the Bots array
                var ip = config["Bots"][0]["Connection"]["IP"].ToString();
                return ip;
            }
            catch (Exception ex)
            {
                // Handle any errors that occur during reading or parsing the file
                Console.WriteLine($"Error reading config file: {ex.Message}");
                return "192.168.1.1"; // Default IP if error occurs
            }
        }

        [Command("addUserSandwich")]
        [Alias("aur", "ra")]
        [Summary("Adds new Sandwich parameter next in the queue.")]
        public async Task AddNewSandwichParamNext(
            [Summary("Seed")] string seed,
            [Summary("Difficulty Level (1-7)")] int level,
            [Summary("Story Progress Level")] int storyProgressLevel = 6,
            [Summary("Species Name or User Mention (Optional)")] string? speciesNameOrUserMention = null,
            [Summary("User Mention 2 (Optional)")] SocketGuildUser? user2 = null,
            [Summary("User Mention 3 (Optional)")] SocketGuildUser? user3 = null)
        {
            var botPrefix = SysCord<T>.Runner.Config.Discord.CommandPrefix;
            if (Hub.Config.RotatingSandwichSV.SandwichSettings.DisableRequests)
            {
                await ReplyAsync("Sandwich Requests are currently disabled by the host.").ConfigureAwait(false);
                return;
            }

            // Check if the first parameter after story progress level is a user mention
            bool isUserMention = speciesNameOrUserMention != null && MyRegex1().IsMatch(speciesNameOrUserMention);
            SocketGuildUser? user1 = null;
            string? speciesName = null;

            if (isUserMention)
            {
                // Extract the user ID from the mention and retrieve the user
                var userId2 = ulong.Parse(Regex.Match(speciesNameOrUserMention, @"\d+").Value);
                user1 = Context.Guild.GetUser(userId2);
            }
            else
            {
                speciesName = speciesNameOrUserMention;
            }

            // Check if private Sandwichs are enabled
            if (!Hub.Config.RotatingSandwichSV.SandwichSettings.PrivateSandwichsEnabled && (user1 != null || user2 != null || user3 != null))
            {
                await ReplyAsync("Private Sandwichs are currently disabled by the host.").ConfigureAwait(false);
                return;
            }
            // Check if the number of user mentions exceeds the limit
            int mentionCount = (user1 != null ? 1 : 0) + (user2 != null ? 1 : 0) + (user3 != null ? 1 : 0);
            if (mentionCount > 3)
            {
                await ReplyAsync("You can only mention up to 3 users for a private Sandwich.").ConfigureAwait(false);
                return;
            }
            var userId = Context.User.Id;
            if (Hub.Config.RotatingSandwichSV.ActiveSandwichs.Any(r => r.RequestedByUserID == userId))
            {
                await ReplyAsync("You already have an existing Sandwich request in the queue.").ConfigureAwait(false);
                return;
            }
            var userRequestManager = new UserRequestManager();
            var userRoles = (Context.User as SocketGuildUser)?.Roles.Select(r => r.Id) ?? new List<ulong>();

            if (!Hub.Config.RotatingSandwichSV.SandwichSettings.BypassLimitRequests.ContainsKey(userId) &&
                !userRoles.Any(Hub.Config.RotatingSandwichSV.SandwichSettings.BypassLimitRequests.ContainsKey))
            {
                if (!userRequestManager.CanRequest(userId, Hub.Config.RotatingSandwichSV.SandwichSettings.LimitRequests, Hub.Config.RotatingSandwichSV.SandwichSettings.LimitRequestsTime, out var remainingCooldown))
                {
                    string responseMessage = $"You have reached your request limit. Please wait {remainingCooldown.TotalMinutes:N0} minutes before making another request.";

                    if (!string.IsNullOrWhiteSpace(Hub.Config.RotatingSandwichSV.SandwichSettings.LimitRequestMsg))
                    {
                        responseMessage += $"\n{Hub.Config.RotatingSandwichSV.SandwichSettings.LimitRequestMsg}";
                    }

                    await ReplyAsync(responseMessage).ConfigureAwait(false);
                    return;
                }
            }

            var settings = Hub.Config.RotatingSandwichSV;
            bool isEvent = !string.IsNullOrEmpty(speciesName);
            var crystalType = level switch
            {
                >= 1 and <= 5 => isEvent ? (TeraCrystalType)2 : 0,
                6 => (TeraCrystalType)1,
                7 => (TeraCrystalType)3,
                _ => throw new ArgumentException("Invalid difficulty level.")
            };

            int SandwichDeliveryGroupID = -1;

            int effectiveQueuePosition = 1;
            var description = string.Empty;
            var prevpath = "bodyparam.txt";
            var filepath = "SandwichFilesSV\\bodyparam.txt";
            if (File.Exists(prevpath))
                Directory.Move(filepath, prevpath + Path.GetFileName(filepath));

            if (File.Exists(filepath))
                description = File.ReadAllText(filepath);

            var data = string.Empty;
            var prevpk = "pkparam.txt";
            var pkpath = "SandwichFilesSV\\pkparam.txt";
            if (File.Exists(prevpk))
                Directory.Move(pkpath, prevpk + Path.GetFileName(pkpath));

            if (File.Exists(pkpath))
                data = File.ReadAllText(pkpath);

            RotatingSandwichParameters newparam = new()
            {
                AddedByRACommand = true,
                RequestCommand = $"{botPrefix}ra {seed} {level} {storyProgressLevel}{(isEvent ? $" {speciesName}" : "")}",
                RequestedByUserID = Context.User.Id,
                Title = $"{Context.User.Username}'s Requested Sandwich{(isEvent ? $" ({speciesName} Event Sandwich)" : "")}",
                SandwichUpNext = false,
                User = Context.User,
                MentionedUsers = new List<SocketUser> { user1, user2, user3 }.Where(u => u != null).ToList(),
            };
            // Determine the correct position to insert the new Sandwich after the current rotation
            int insertPosition = RotationCount + 1;
            while (insertPosition < Hub.Config.RotatingSandwichSV.ActiveSandwichs.Count && Hub.Config.RotatingSandwichSV.ActiveSandwichs[insertPosition].AddedByRACommand)
            {
                insertPosition++;
            }
            // Set SandwichUpNext to true only if the new Sandwich is inserted immediately next in the rotation
            if (insertPosition == RotationCount + 1)
            {
                newparam.SandwichUpNext = true;
            }
            // After the new Sandwich is inserted
            Hub.Config.RotatingSandwichSV.ActiveSandwichs.Insert(insertPosition, newparam);

            // Adjust RotationCount
            if (insertPosition <= RotationCount)
            {
                RotationCount++;
            }

            // Calculate the user's position in the queue and the estimated wait time
            effectiveQueuePosition = CalculateEffectiveQueuePosition(Context.User.Id, RotationCount);
            int etaMinutes = effectiveQueuePosition * 6;

            var queuePositionMessage = effectiveQueuePosition > 0
                ? $"You are currently {effectiveQueuePosition} in the queue with an estimated wait time of {etaMinutes} minutes."
                : "Your Sandwich request is up next!";

            var replyMsg = $"{Context.User.Mention}, added your Sandwich to the queue! I'll DM you when it's about to start.";

            // Notify the mentioned users
            var mentionedUsers = new List<SocketGuildUser>();
            if (user1 != null) mentionedUsers.Add(user1);
            if (user2 != null) mentionedUsers.Add(user2);
            if (user3 != null) mentionedUsers.Add(user3);

            foreach (var user in mentionedUsers)
            {
                try
                {
                    await user.SendMessageAsync($"{Context.User.Username} invited you to a private Sandwich! I'll DM you the code when it's about to start.", false).ConfigureAwait(false);
                }
                catch
                {
                    await ReplyAsync($"Failed to send DM to {user.Mention}. Please make sure their DMs are open.").ConfigureAwait(false);
                }
            }
            try
            {
                if (Context.User is SocketGuildUser user)
                {
                    await user.SendMessageAsync($"Here's your Sandwich information:\n{queuePositionMessage}\nYour request command: `{newparam.RequestCommand}`", false).ConfigureAwait(false);
                }
                else
                {
                    await ReplyAsync("Failed to send DM. Please make sure your DMs are open.").ConfigureAwait(false);
                }
            }
            catch
            {
                await ReplyAsync("Failed to send DM. Please make sure your DMs are open.").ConfigureAwait(false);
            }
        }

        [Command("SandwichQueueStatus")]
        [Alias("rqs")]
        [Summary("Checks the number of Sandwichs before the user's request and gives an ETA.")]
        public async Task CheckQueueStatus()
        {
            var userId = Context.User.Id;
            int currentPosition = RotationCount;

            // Find the index of the user's request in the queue, excluding Mystery Shiny Sandwichs
            var userRequestIndex = Hub.Config.RotatingSandwichSV.ActiveSandwichs.FindIndex(r => r.RequestedByUserID == userId && !r.Title.Contains("Mystery Shiny Sandwich"));

            EmbedBuilder embed = new();

            if (userRequestIndex == -1)
            {
                embed.Title = "Queue Status";
                embed.Color = Color.Red;
                embed.Description = $"{Context.User.Mention}, you do not have a Sandwich request in the queue.";
            }
            else
            {
                // Calculate the effective position of the user's request in the queue
                int SandwichsBeforeUser = CalculateEffectiveQueuePosition(userId, currentPosition);

                if (SandwichsBeforeUser <= 0)
                {
                    embed.Title = "Queue Status";
                    embed.Color = Color.Green;
                    embed.Description = $"{Context.User.Mention}, your Sandwich request is up next!";
                }
                else
                {
                    // Calculate ETA assuming each Sandwich takes 6 minutes
                    int etaMinutes = SandwichsBeforeUser * 6;

                    embed.Title = "Queue Status";
                    embed.Color = Color.Orange;
                    embed.Description = $"{Context.User.Mention}, here's the status of your Sandwich request:";
                    embed.AddField("Sandwichs Before Yours", SandwichsBeforeUser.ToString(), true);
                    embed.AddField("Estimated Time", $"{etaMinutes} minutes", true);
                }
            }

            await Context.Message.DeleteAsync().ConfigureAwait(false);
            await ReplyAsync(embed: embed.Build()).ConfigureAwait(false);
        }

        private int CalculateEffectiveQueuePosition(ulong userId, int currentPosition)
        {
            int effectivePosition = 0;
            bool userRequestFound = false;

            for (int i = currentPosition; i < Hub.Config.RotatingSandwichSV.ActiveSandwichs.Count + currentPosition; i++)
            {
                int actualIndex = i % Hub.Config.RotatingSandwichSV.ActiveSandwichs.Count;
                var Sandwich = Hub.Config.RotatingSandwichSV.ActiveSandwichs[actualIndex];

                // Check if the Sandwich is added by the RA command and is not a Mystery Shiny Sandwich
                if (Sandwich.AddedByRACommand && !Sandwich.Title.Contains("Mystery Shiny Sandwich"))
                {
                    if (Sandwich.RequestedByUserID == userId)
                    {
                        // Found the user's request
                        userRequestFound = true;
                        break;
                    }
                    else if (!userRequestFound)
                    {
                        // Count other user requested Sandwichs before the user's request
                        effectivePosition++;
                    }
                }
            }

            // If the user's request was not found after the current position, count from the beginning
            if (!userRequestFound)
            {
                for (int i = 0; i < currentPosition; i++)
                {
                    var Sandwich = Hub.Config.RotatingSandwichSV.ActiveSandwichs[i];
                    if (Sandwich.AddedByRACommand && !Sandwich.Title.Contains("Mystery Shiny Sandwich"))
                    {
                        if (Sandwich.RequestedByUserID == userId)
                        {
                            // Found the user's request
                            break;
                        }
                        else
                        {
                            effectivePosition++;
                        }
                    }
                }
            }

            return effectivePosition;
        }

        [Command("SandwichQueueClear")]
        [Alias("rqc")]
        [Summary("Removes the Sandwich added by the user.")]
        public async Task RemoveOwnSandwichParam()
        {
            var userId = Context.User.Id;
            var list = Hub.Config.RotatingSandwichSV.ActiveSandwichs;

            // Find the Sandwich added by the user
            var userSandwich = list.FirstOrDefault(r => r.RequestedByUserID == userId && r.AddedByRACommand);
            if (userSandwich == null)
            {
                await ReplyAsync("You don't have a Sandwich added.").ConfigureAwait(false);
                return;
            }

            // Prevent canceling if the Sandwich is up next
            if (userSandwich.SandwichUpNext)
            {
                await ReplyAsync("Your Sandwich request is up next and cannot be canceled at this time.").ConfigureAwait(false);
                return;
            }

            // Remove the Sandwich if it's not up next
            list.Remove(userSandwich);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
            var msg = $"Cleared your Sandwich from the queue.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("removeSandwichParams")]
        [Alias("rrp")]
        [Summary("Removes a Sandwich parameter.")]
        [RequireSudo]
        public async Task RemoveSandwichParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingSandwichSV.ActiveSandwichs;
            if (index >= 0 && index < list.Count)
            {
                var Sandwich = list[index];
                list.RemoveAt(index);
                var msg = $"Sandwich for {Sandwich.Title} has been removed!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid Sandwich parameter index.").ConfigureAwait(false);
        }

        [Command("toggleSandwichParams")]
        [Alias("trp")]
        [Summary("Toggles Sandwich parameter.")]
        [RequireSudo]
        public async Task ToggleSandwichParam([Summary("Seed Index")] int index)
        {
            var list = Hub.Config.RotatingSandwichSV.ActiveSandwichs;
            if (index >= 0 && index < list.Count)
            {
                var Sandwich = list[index];
                Sandwich.ActiveInRotation = !Sandwich.ActiveInRotation;
                var m = Sandwich.ActiveInRotation ? "enabled" : "disabled";
                var msg = $"Sandwich for {Sandwich.Title} has been {m}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid Sandwich parameter Index.").ConfigureAwait(false);
        }

        [Command("changeSandwichParamTitle")]
        [Alias("crpt")]
        [Summary("Changes the title of a  Sandwich parameter.")]
        [RequireSudo]
        public async Task ChangeSandwichParamTitle([Summary("Seed Index")] int index, [Summary("Title")] string title)
        {
            var list = Hub.Config.RotatingSandwichSV.ActiveSandwichs;
            if (index >= 0 && index < list.Count)
            {
                var Sandwich = list[index];
                Sandwich.Title = title;
                var msg = $"Sandwich Title for {Sandwich.Title} has been changed to: {title}!";
                await ReplyAsync(msg).ConfigureAwait(false);
            }
            else
                await ReplyAsync("Invalid Sandwich parameter Index.").ConfigureAwait(false);
        }

        [Command("viewSandwichList")]
        [Alias("vrl", "rotatinglist")]
        [Summary("Prints the Sandwichs in the current collection.")]
        public async Task GetSandwichListAsync()
        {
            var list = Hub.Config.RotatingSandwichSV.ActiveSandwichs;
            int count = list.Count;
            int fields = (int)Math.Ceiling((double)count / 15);
            var embed = new EmbedBuilder
            {
                Title = "Sandwich List"
            };
            for (int i = 0; i < fields; i++)
            {
                int start = i * 15;
                int end = Math.Min(start + 14, count - 1);
                var fieldBuilder = new StringBuilder();
                for (int j = start; j <= end; j++)
                {
                    var Sandwich = list[j];
                    int paramNumber = j;
                    fieldBuilder.AppendLine($"{paramNumber}.) {Sandwich.Title} - Status: {(Sandwich.ActiveInRotation ? "Active" : "Inactive")}");
                }
                embed.AddField($"Sandwich List - Part {i + 1}", fieldBuilder.ToString(), false);
            }
            await ReplyAsync($"These are the Sandwichs currently in the list (total: {count}):", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("Sandwichhelp")]
        [Alias("rh")]
        [Summary("Prints the Sandwich help command list.")]
        public async Task GetSandwichHelpListAsync()
        {
            var embed = new EmbedBuilder();
            List<string> cmds = new()
            {
                "$ban - Ban a user from Sandwichs via NID. [Command] [OT] - Sudo only command.\n",
                "$vrl - View all Sandwichs in the list.\n",
                "$arp - Add parameter to the collection.\nEx: [Command] [Index] [Species] [Difficulty]\n",
                "$rrp - Remove parameter from the collection.\nEx: [Command] [Index]\n",
                "$trp - Toggle the parameter as Active/Inactive in the collection.\nEx: [Command] [Index]\n",
                "$tcrp - Toggle the parameter as Coded/Uncoded in the collection.\nEx: [Command] [Index]\n",
                "$trpk - Set a PartyPK for the parameter via a showdown set.\nEx: [Command] [Index] [ShowdownSet]\n",
                "$crpt - Set the title for the parameter.\nEx: [Command] [Index]"
            };
            string msg = string.Join("", cmds.ToList());
            embed.AddField(x =>
            {
                x.Name = "Sandwich Help Commands";
                x.Value = msg;
                x.IsInline = false;
            });
            await ReplyAsync("Here's your Sandwich help!", embed: embed.Build()).ConfigureAwait(false);
        }

        [Command("unbanrotatingSandwicher")]
        [Alias("ubrr")]
        [Summary("Removes the specificed NID from the banlist for Sandwichs in SV.")]
        [RequireSudo]
        public async Task UnbanRotatingSandwicher([Summary("Removes the specificed NID from the banlist for Sandwichs in SV.")] string nid)
        {
            var list = Hub.Config.RotatingSandwichSV.SandwicherBanList.List.ToArray();
            string msg = $"{Context.User.Mention} no user found with that NID.";
            for (int i = 0; i < list.Length; i++)
                if ($"{list[i].ID}".Equals(nid))
                {
                    msg = $"{Context.User.Mention} user {list[i].Name} - {list[i].ID} has been unbanned.";
                    Hub.Config.RotatingSandwichSV.SandwicherBanList.List.ToList().Remove(list[i]);
                }
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [GeneratedRegex(@"^<@!?\d+>$")]
        private static partial Regex MyRegex1();
    }
}