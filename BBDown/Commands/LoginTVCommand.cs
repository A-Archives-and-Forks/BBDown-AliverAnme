using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class LoginTVCommand : Command<LoginSettings>
{
    protected override int Execute(CommandContext context, LoginSettings settings, CancellationToken cancellationToken)
    {
        // Task.Run avoids deadlock if called from a thread with a SynchronizationContext
        return Task.Run(() => BBDownLoginUtil.LoginTV(cancellationToken)).GetAwaiter().GetResult() ? 0 : 1;
    }
}
