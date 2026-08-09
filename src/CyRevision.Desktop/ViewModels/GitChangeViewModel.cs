using CyRevision.Git;

namespace CyRevision.Desktop.ViewModels;

public sealed class GitChangeViewModel(GitChange change)
{
    public GitChange Change { get; } = change;

    public string Path => Change.Path;

    public string State => Change.Kind switch
    {
        GitChangeKind.Added => "Ajouté",
        GitChangeKind.Modified => "Modifié",
        GitChangeKind.Deleted => "Supprimé",
        GitChangeKind.Renamed => "Renommé",
        GitChangeKind.Untracked => "Non suivi",
        GitChangeKind.Conflicted => "Conflit",
        _ => Change.Kind.ToString()
    };

    public string Area => Change.IsStaged ? "Index" : "Travail";

    public string Lfs => Change.IsLfsObject ? "LFS" : string.Empty;
}

