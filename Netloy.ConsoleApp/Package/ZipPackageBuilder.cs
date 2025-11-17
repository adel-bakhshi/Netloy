using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;

namespace Netloy.ConsoleApp.Package;

public class ZipPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    public string PublishOutputDir { get; } = string.Empty;

    public ZipPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
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