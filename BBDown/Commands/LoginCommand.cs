using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)]
public class LoginSettings : CommandSettings
{
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class LoginCommand : Command<LoginSettings>
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(LoginSettings))]
    protected override int Execute(CommandContext context, LoginSettings settings, CancellationToken cancellationToken)
    {
        return BBDownLoginUtil.LoginWEB().GetAwaiter().GetResult() ? 0 : 1;
    }
}
