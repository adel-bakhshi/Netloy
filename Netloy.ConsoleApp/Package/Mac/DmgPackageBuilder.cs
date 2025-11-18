using Netloy.ConsoleApp.Argument;
using Netloy.ConsoleApp.Configuration;

namespace Netloy.ConsoleApp.Package.Mac;

public class DmgPackageBuilder : PackageBuilderBase, IPackageBuilder
{
    public string PublishOutputDir { get; }

    public DmgPackageBuilder(Arguments arguments, Configurations configurations) : base(arguments, configurations)
    {
    }

    public async Task BuildAsync()
    {
        throw new NotImplementedException();
    }

    public bool Validate()
    {
        throw new NotImplementedException();
    }

    public void Clear()
    {
        throw new NotImplementedException();
    }
}