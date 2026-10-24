using Avalonia.Media.Imaging;
namespace CyRevision.Desktop.ViewModels;
public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string,(Bitmap Image,string Path)> _nativeImages=[];
    public string ExportNativeImage(Bitmap? image,string slot)
    {
        if(image is null)return "";
        if(_nativeImages.TryGetValue(slot,out var old) && ReferenceEquals(old.Image,image) && File.Exists(old.Path))return old.Path;
        string directory=Path.Combine(_applicationPaths.CacheDirectory,"native","previews");
        string path=Path.Combine(directory,$"{Guid.NewGuid():N}.png");
        try{Directory.CreateDirectory(directory);image.Save(path);_nativeImages[slot]=(image,path);if(old.Path is not null)File.Delete(old.Path);return path;}
        catch(Exception error)when(error is IOException or UnauthorizedAccessException or ObjectDisposedException){return "";}
    }
}
