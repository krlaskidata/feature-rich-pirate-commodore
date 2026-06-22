using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    /// <summary>
    /// Restricts a command to users who have the designated Admin role
    /// (ID 1395340423018319892) or Discord's built-in Administrator permission.
    /// </summary>
    public class RequirePirateAdminAttribute : PreconditionAttribute
    {
        private const ulong AdminRoleId = 1395340423018319892UL;

        public override Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is not SocketGuildUser guildUser)
                return Task.FromResult(PreconditionResult.FromError(
                    "❌ This command can only be used inside a server."));

            if (guildUser.GuildPermissions.Administrator ||
                guildUser.Roles.Any(r => r.Id == AdminRoleId))
                return Task.FromResult(PreconditionResult.FromSuccess());

            return Task.FromResult(PreconditionResult.FromError(
                "❌ Ye need the **Admin** role to use this command, matey! 🏴‍☠️"));
        }
    }
}
