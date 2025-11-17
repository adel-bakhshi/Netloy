namespace Netloy.ConsoleApp.Package;

public interface IPackageBuilder
{
    string PublishOutputDir { get; }
    
    Task BuildAsync();

    void Clear();
}