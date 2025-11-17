namespace Netloy.ConsoleApp.Package;

public interface IPackageBuilder
{
    string PublishOutputDir { get; }

    Task BuildAsync();

    bool Validate();

    void Clear();
}