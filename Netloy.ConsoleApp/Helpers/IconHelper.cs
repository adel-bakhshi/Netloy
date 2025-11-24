using Netloy.ConsoleApp.Configuration;
using Netloy.ConsoleApp.Extensions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace Netloy.ConsoleApp.Helpers;

public class IconHelper
{
    private readonly Configurations _configurations;
    private readonly IReadOnlyList<int> _standardIconSizes;
    private readonly string _iconsDirectory;

    public IconHelper() : this(new Configurations(), string.Empty)
    {
    }

    public IconHelper(Configurations configurations, string iconsDirectory)
    {
        _configurations = configurations;
        _iconsDirectory = iconsDirectory;
        _standardIconSizes = [16, 24, 32, 48, 64, 96, 128, 256, 512, 1024];
    }

    public async Task GenerateIconsAsync()
    {
        if (_configurations.AutoGenerateIcons)
        {
            var defaultIconPath = _configurations.IconsCollection.Find(ico => ico.EndsWith(".1024x1024.png", StringComparison.OrdinalIgnoreCase));
            if (defaultIconPath.IsStringNullOrEmpty())
                throw new InvalidOperationException("No .svg or .1024x1024.png icon found. Please provide a default icon in the configuration file.");

            // Remove png icons from the list
            _configurations.IconsCollection = _configurations.IconsCollection
                .Where(ico => !ico.Contains(".png", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var remainingIcons = _configurations.IconsCollection.ToList();

            await ResizeImageAsync(defaultIconPath!, 16);
            await ResizeImageAsync(defaultIconPath!, 24);
            await ResizeImageAsync(defaultIconPath!, 32);
            await ResizeImageAsync(defaultIconPath!, 48);
            await ResizeImageAsync(defaultIconPath!, 64);
            await ResizeImageAsync(defaultIconPath!, 96);
            await ResizeImageAsync(defaultIconPath!, 128);
            await ResizeImageAsync(defaultIconPath!, 256);
            await ResizeImageAsync(defaultIconPath!, 512);
            await ResizeImageAsync(defaultIconPath!, 1024);

            foreach (var iconFile in remainingIcons)
            {
                var ext = Path.GetExtension(iconFile).ToLowerInvariant();
                var finalIconPath = Path.Combine(_iconsDirectory, _configurations.AppBaseName + ext);
                File.Copy(iconFile, finalIconPath, overwrite: true);
                var originalIcon = _configurations.IconsCollection.Find(ico => Path.GetExtension(ico).Equals(ext, StringComparison.OrdinalIgnoreCase));
                if (originalIcon.IsStringNullOrEmpty())
                    continue;

                var index = _configurations.IconsCollection.IndexOf(originalIcon!);
                _configurations.IconsCollection[index] = finalIconPath;
            }
        }
        else
        {
            for (var i = 0; i < _configurations.IconsCollection.Count; i++)
            {
                var iconFile = _configurations.IconsCollection[i];

                string finalIconPath;
                var fileName = Path.GetFileNameWithoutExtension(iconFile);
                var ext = Path.GetExtension(iconFile).ToLowerInvariant();
                if (ext.Equals(".png"))
                {
                    var sections = fileName.Split(".");
                    var sizeSection = sections[1].Split("x");
                    var width = int.Parse(sizeSection[0]);
                    if (!_standardIconSizes.Contains(width))
                        continue;

                    finalIconPath = Path.Combine(_iconsDirectory, $"{_configurations.AppBaseName}.{sections[1]}.{ext}");
                }
                else
                {
                    finalIconPath = Path.Combine(_iconsDirectory, $"{_configurations.AppBaseName}.{ext}");
                }

                File.Copy(iconFile, finalIconPath, overwrite: true);
                _configurations.IconsCollection[i] = finalIconPath;
            }
        }
    }

    public static async Task<Size> GetImageSizeAsync(string imagePath)
    {
        using var image = await Image.LoadAsync(imagePath);
        return image.Size;
    }

    public List<string> GetIconSizes()
    {
        var iconSizes = _standardIconSizes.Select(s => $"{s}x{s}").ToList();
        iconSizes.Add("scalable");

        return iconSizes;
    }

    private async Task ResizeImageAsync(string inputPath, int size)
    {
        var outputPath = Path.Combine(_iconsDirectory, $"{_configurations.AppBaseName}.{size}x{size}.png");

        using var image = await Image.LoadAsync(inputPath);
        image.Mutate(op => op.Resize(size, size));
        await image.SaveAsync(outputPath);

        _configurations.IconsCollection.Add(outputPath);
    }
}