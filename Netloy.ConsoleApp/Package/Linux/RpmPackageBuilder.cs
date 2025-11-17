using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;

namespace Netloy.ConsoleApp.Package.Linux;

public class RpmPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    public string PublishOutputDir { get; } = string.Empty;

    public RpmPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
    }

    public async Task BuildAsync()
    {
        throw new NotImplementedException();
    }

    public void Clear()
    {
        throw new NotImplementedException();
    }
}