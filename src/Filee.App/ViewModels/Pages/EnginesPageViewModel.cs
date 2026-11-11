// Conversion engines: optional downloads, availability and priority order. Filee only uses its own engine copies,
// so there are no paths to configure.

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Filee.App.Services;
using Filee.Core.Conversion;
using Filee.Core.Localization;
using Filee.Core.Settings;
using Filee.Engines.Infrastructure;

namespace Filee.App.ViewModels.Pages;

public sealed partial class EngineItemViewModel(IConverter converter, EngineStatus status, ILocalizer loc) : ObservableObject
{
    /// <summary>Being dragged to a new place in the priority order.</summary>
    [ObservableProperty] private bool _isDragging;

    public IConverter Converter { get; } = converter;
    /// <summary>Localized descriptive name, retaining the engine's technical name for unregistered engines.</summary>
    public string Name
    {
        get
        {
            var key = $"engine.{Converter.Id}.name";
            return loc[key] is var text && text != key ? text : Converter.DisplayName;
        }
    }
    public bool IsAvailable { get; } = status.IsAvailable;
    public string StatusText { get; } = status.IsAvailable ? loc["engines.available"] : loc[status.ReasonKey ?? "engines.unavailable"];

    /// <summary>Path of the program or other technical detail, shown as a tooltip.</summary>
    public string? Detail { get; } = status.Detail;

    /// <summary>"Markdig 1.4.0", "0.8.6" — what the engine reports; empty for engines that aren't installed.</summary>
    public string? Version { get; } = status.Version;

    /// <summary>What the engine converts (engine.&lt;id&gt;.description), or null when there is no text for it.</summary>
    public string? Description { get; } = Describe(loc, converter.Id);

    public string Conversions { get; } = loc.Format("engines.conversions", converter.Edges.Count);

    private static string? Describe(ILocalizer loc, string id)
    {
        var key = $"engine.{id}.description";
        var text = loc[key];
        return text == key ? null : text;
    }
}

public sealed partial class EnginesPageViewModel : ObservableObject, IDisposable
{
    private readonly UserDataStore _store;
    private readonly ConverterCatalog _catalog;
    private readonly ILocalizer _loc;
    private readonly EngineDownloadService _downloads;
    private readonly JobQueue? _queue;

    public EnginesPageViewModel(UserDataStore store, ConverterCatalog catalog, ILocalizer loc, EngineDownloadService downloads)
    {
        _store = store;
        _catalog = catalog;
        _loc = loc;
        _downloads = downloads;
        _libreOfficeForSlides = store.Settings.LibreOfficeForSlides;
        _libreOfficeForHwp = store.Settings.LibreOfficeForHwp;
        _libreOfficeForDocuments = store.Settings.LibreOfficeForDocuments;
        _libreOfficeForSheets = store.Settings.LibreOfficeForSheets;
        _catalog.Changed += OnCatalogChanged;

        _queue = AppHost.TryGet<JobQueue>();
        if (_queue != null)
        {
            _isConverting = _queue.IsBusy;
            _queue.JobUpdated += OnJobQueueStateChanged;
            _queue.JobFinished += OnJobQueueStateChanged;
        }

        Reload();
        UpdateFilteredLists();
    }

    /// <summary>Presentations → PDF through LibreOffice when it is installed (App.ApplySettings re-checks the engines).</summary>
    [ObservableProperty] private bool _libreOfficeForSlides;

    /// <summary>HWP / HWPX → PDF through LibreOffice and H2Orestart instead of rhwp.</summary>
    [ObservableProperty] private bool _libreOfficeForHwp;

    /// <summary>Word documents → PDF through LibreOffice instead of the built-in route.</summary>
    [ObservableProperty] private bool _libreOfficeForDocuments;

    /// <summary>Spreadsheets → PDF through LibreOffice instead of the built-in route.</summary>
    [ObservableProperty] private bool _libreOfficeForSheets;

    partial void OnLibreOfficeForSlidesChanged(bool value)
    {
        _store.Settings.LibreOfficeForSlides = value;
        _store.SaveSettings();
    }

    partial void OnLibreOfficeForHwpChanged(bool value)
    {
        _store.Settings.LibreOfficeForHwp = value;
        _store.SaveSettings();
    }

    partial void OnLibreOfficeForDocumentsChanged(bool value)
    {
        _store.Settings.LibreOfficeForDocuments = value;
        _store.SaveSettings();
    }

    partial void OnLibreOfficeForSheetsChanged(bool value)
    {
        _store.Settings.LibreOfficeForSheets = value;
        _store.SaveSettings();
    }

    /// <summary>Optional engines that are downloaded on demand.</summary>
    public IReadOnlyList<EnginePackageState> Packages => _downloads.Packages;

    public ObservableCollection<EngineItemViewModel> Engines { get; } = [];

    /// <summary>Active category in the pipeline visualization (0: Graphics, 1: Audio &amp; Video, 2: Office &amp; Docs).</summary>
    [ObservableProperty] private int _selectedCategory = 1;

    /// <summary>Whether the bottom console bar dropdown is expanded.</summary>
    [ObservableProperty] private bool _isDropdownOpen = true;

    /// <summary>Active conversion state forwarded from JobQueue to accelerate pipeline particle animations.</summary>
    [ObservableProperty] private bool _isConverting;

    private void OnJobQueueStateChanged(object? sender, ConversionJob e)
    {
        if (_queue != null)
        {
            var busy = _queue.IsBusy;
            if (IsConverting != busy)
            {
                Dispatcher.UIThread.Post(() => IsConverting = busy);
            }
        }
    }

    public ObservableCollection<EnginePackageState> FilteredPackages { get; } = [];
    public ObservableCollection<EngineItemViewModel> FilteredEngines { get; } = [];

    partial void OnSelectedCategoryChanged(int value) => UpdateFilteredLists();

    partial void OnIsDropdownOpenChanged(bool value)
    {
        OnPropertyChanged(nameof(DropdownGlyph));
        OnPropertyChanged(nameof(DropdownToggleHint));
    }

    public string DropdownGlyph => IsDropdownOpen ? "▼" : "›";
    public string DropdownToggleHint => IsDropdownOpen ? "Hide engines ▲" : "View engines ▼";

    public string ActiveStatusText => SelectedCategory switch
    {
        0 => "Graphics & Images: ImageMagick & PDFium — 1,120 conversions ready",
        1 => "Audio & Video: FFmpeg Engine — Real-time stream transcoding & audio pipeline active",
        _ => "Office & Documents: LibreOffice, Pandoc & rhwp — High-fidelity document pipeline active"
    };

    public string CategoryTitle => SelectedCategory switch
    {
        0 => "Graphics & Image Engines",
        1 => "Audio & Video Engines",
        _ => "Office & Document Engines"
    };

    public string CategoryDescription => SelectedCategory switch
    {
        0 => "ImageMagick, PDFium, Ghostscript and raster/vector processors.",
        1 => "FFmpeg media stream transcoder and audio pipeline.",
        _ => "LibreOffice, Pandoc, rhwp, Calibre and document processors."
    };

    [RelayCommand]
    public void ToggleDropdown() => IsDropdownOpen = !IsDropdownOpen;

    [RelayCommand]
    public void SelectCategory(int category)
    {
        SelectedCategory = category;
        IsDropdownOpen = true;
    }

    public void UpdateFilteredLists()
    {
        FilteredPackages.Clear();
        foreach (var pkg in Packages)
        {
            if (GetCategoryForPackageId(pkg.Package.Id) == SelectedCategory)
                FilteredPackages.Add(pkg);
        }

        FilteredEngines.Clear();
        foreach (var eng in Engines)
        {
            if (GetCategoryForEngineId(eng.Converter.Id) == SelectedCategory)
                FilteredEngines.Add(eng);
        }

        OnPropertyChanged(nameof(ActiveStatusText));
        OnPropertyChanged(nameof(CategoryTitle));
        OnPropertyChanged(nameof(CategoryDescription));
        OnPropertyChanged(nameof(DropdownGlyph));
        OnPropertyChanged(nameof(DropdownToggleHint));
    }

    public static int GetCategoryForPackageId(string id)
    {
        var lower = id.ToLowerInvariant();
        if (lower.Contains("ffmpeg")) return 1;
        if (lower.Contains("ghostscript")) return 0;
        return 2;
    }

    public static int GetCategoryForEngineId(string id)
    {
        var lower = id.ToLowerInvariant();
        if (lower.Contains("ffmpeg") || lower.Contains("whisper") || lower.Contains("audio") || lower.Contains("video") || lower.Contains("subtitle"))
            return 1;
        if (lower.Contains("magick") || lower.Contains("image") || lower.Contains("pdfium") || lower.Contains("ghostscript") || lower.Contains("vector") || lower.Contains("font") || lower.Contains("icns"))
            return 0;
        return 2;
    }

    // Any number of rows can be clicked; the service queues them and installs one at a time (#819).
    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task Install(EnginePackageState package) => _downloads.InstallAsync(package);

    [RelayCommand]
    private void Cancel(EnginePackageState package) => _downloads.Cancel(package);

    /// <summary>Installs a package from files the user downloaded elsewhere (#447); a failure shows on its card.</summary>
    public Task InstallFromFilesAsync(EnginePackageState package, IReadOnlyList<string> files) => _downloads.InstallAsync(package, files);

    /// <summary>Saves a package's downloads in <paramref name="folder"/> for a computer without internet (#447).</summary>
    public Task<(bool Saved, string? Error)> SaveFilesAsync(EnginePackageState package, string folder) => _downloads.SaveFilesAsync(package, folder);

    /// <summary>Name, URL and SHA-256 of each of a package's downloads, to fetch them elsewhere (#447).</summary>
    public static string DownloadLinks(EnginePackageState package) => EngineDownloads.DownloadList(package.Package, EngineDownloads.Components);

    /// <summary>Removes a package (after the view asked for confirmation). False when its files are in use.</summary>
    public bool Remove(EnginePackageState package) => _downloads.Uninstall(package);

    /// <summary>
    /// Uses the user's own copy of a package from <paramref name="path"/> (a program they picked). Returns false when
    /// that folder doesn't have the package's programs; nothing is saved then.
    /// </summary>
    public bool UseOwnCopy(EnginePackageState package, string path)
    {
        if (EngineEnvironment.FolderWithPrograms(package.Package.Id, path) is null)
            return false;
        _store.Settings.EngineOwnCopies[package.Package.Id] = path;
        _store.SaveSettings(); // App.ApplySettings re-checks the engines
        return true;
    }

    /// <summary>Goes back to Filee's download (or no engine) for <paramref name="package"/>.</summary>
    public void StopUsingOwnCopy(EnginePackageState package)
    {
        if (_store.Settings.EngineOwnCopies.Remove(package.Package.Id))
            _store.SaveSettings();
    }

    // Installing or removing an engine changes the list below (status, supported conversions).
    private void OnCatalogChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Reload);

    public void Dispose()
    {
        _catalog.Changed -= OnCatalogChanged;
        if (_queue != null)
        {
            _queue.JobUpdated -= OnJobQueueStateChanged;
            _queue.JobFinished -= OnJobQueueStateChanged;
        }
    }

    private void Reload()
    {
        Engines.Clear();
        foreach (var converter in _catalog.Ordered)
            Engines.Add(new EngineItemViewModel(converter, _catalog.StatusOf(converter), _loc));
        UpdateFilteredLists();
    }

    [RelayCommand]
    private void MoveUp(EngineItemViewModel item) => Move(item, -1);

    [RelayCommand]
    private void MoveDown(EngineItemViewModel item) => Move(item, +1);

    /// <summary>The check can be over in a blink; the arrow on "Check again" keeps turning for at least this long so it reads as checking.</summary>
    private static readonly TimeSpan MinimumRecheckTime = TimeSpan.FromMilliseconds(900);

    [RelayCommand]
    private async Task Recheck()
    {
        // With "Reduce animations" on there is no arrow turning, so there is nothing to hold the result back for.
        var minimumSpin = Task.Delay(Motion.Enabled ? MinimumRecheckTime : TimeSpan.Zero);
        EngineEnvironment.ForgetSystemCopies(); // a tool installed meanwhile is found
        _downloads.RefreshStatus();
        _catalog.Refresh();
        UpdateFilteredLists();
        await minimumSpin;
    }

    private void Move(EngineItemViewModel item, int delta)
    {
        if (MoveTo(item, Engines.IndexOf(item) + delta))
        {
            SavePriority();
            UpdateFilteredLists();
        }
    }

    /// <summary>Moves <paramref name="item"/> to <paramref name="index"/> in the list (while dragging); false when it stays.</summary>
    public bool MoveTo(EngineItemViewModel item, int index)
    {
        var from = Engines.IndexOf(item);
        if (from < 0 || index < 0 || index >= Engines.Count || index == from)
            return false;
        Engines.Move(from, index);
        return true;
    }

    /// <summary>Saves the order of the list as the engine priority.</summary>
    public void SavePriority()
    {
        _store.Settings.EnginePriority = Engines.Select(e => e.Converter.Id).ToList();
        _store.SaveSettings();
    }
}
