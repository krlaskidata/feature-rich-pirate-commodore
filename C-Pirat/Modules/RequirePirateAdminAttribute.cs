using System;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;

namespace PiratBotCSharp.Modules
{
    public class RequirePirateAdminAttribute : PreconditionAttribute
    {
        public override Task<PreconditionResult> CheckPermissionsAsync(
            ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            if (context.User is not SocketGuildUser guildUser || context.Guild is not SocketGuild guild)
                return Task.FromResult(PreconditionResult.FromError(
                    "❌ This command can only be used inside a server."));

            if (guildUser.GuildPermissions.Administrator)
                return Task.FromResult(PreconditionResult.FromSuccess());

            var config = SecurityService.GetConfig(guild.Id);
            if (config.AdminRoleIds.Any() && guildUser.Roles.Any(r => config.AdminRoleIds.Contains(r.Id)))
                return Task.FromResult(PreconditionResult.FromSuccess());

            return Task.FromResult(PreconditionResult.FromError(
                "❌ Ye need an **Admin** role to use this command, matey! 🏴‍☠️"));
        }
    }
}
