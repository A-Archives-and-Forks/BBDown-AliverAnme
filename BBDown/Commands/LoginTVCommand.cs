using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class LoginTVCommand : Command<LoginSettings>
{
    protected override int Execute(CommandContext context, LoginSettings settings, CancellationToken cancellationToken)
    {
        return BBDownLoginUtil.LoginTV().GetAwaiter().GetResult() ? 0 : 1;
    }
}
