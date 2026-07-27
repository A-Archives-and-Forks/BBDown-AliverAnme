using Spectre.Console.Cli;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class DefaultCommand : AsyncCommand<MyOption>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, MyOption settings, CancellationToken cancellationToken)
    {
        _ = BBDownUtil.CheckUpdateAsync();
        await Program.DoWorkAsync(settings, cancellationToken);
        return 0;
    }
}
