using System.Collections.ObjectModel;

namespace CyRevision.Desktop.ViewModels;

public sealed class ProjectSidebarGroupViewModel : ObservableObject
{
    private bool _isExpanded;
    private ProjectItemViewModel? _selectedProject;

    public ProjectSidebarGroupViewModel(string name, IEnumerable<ProjectItemViewModel> projects, bool isExpanded)
    {
        Name = name;
        Projects = new ObservableCollection<ProjectItemViewModel>(projects);
        _isExpanded = isExpanded;
    }

    public string Name { get; }
    public ObservableCollection<ProjectItemViewModel> Projects { get; }
    public string CountText => $"{Projects.Count:N0}";

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    public ProjectItemViewModel? SelectedProject
    {
        get => _selectedProject;
        set => SetProperty(ref _selectedProject, value);
    }
}
