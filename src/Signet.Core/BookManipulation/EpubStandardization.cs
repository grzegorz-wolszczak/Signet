using System;
using System.Collections.Generic;
using System.Linq;
using Signet.Core.Resources;
using SysPath = System.IO.Path;

namespace Signet.Core.BookManipulation;

/// <summary>A step of the "Standardize EPUB" dialog, in the order the steps run.</summary>
public enum StandardizationStep
{
    /// <summary>Move every file to its standard folder (<c>OEBPS/Text</c>, <c>OEBPS/Styles</c>, …).</summary>
    StandardFolders,

    /// <summary>Give every file the extension that matches its media type.</summary>
    StandardFileExtensions,

    /// <summary>Base the OPF manifest identifiers on the file names.</summary>
    RebaseManifestIds,

    /// <summary>Set the OPF manifest <c>media-type</c> of every file to its actual media type.</summary>
    ManifestMediaTypes,
}

/// <summary>What a <see cref="StandardizationChange"/> changes.</summary>
public enum StandardizationChangeKind
{
    /// <summary>The bookpath of a file (a new name and/or folder).</summary>
    Path,

    /// <summary>The identifier of a manifest entry.</summary>
    ManifestId,

    /// <summary>The <c>media-type</c> of a manifest entry.</summary>
    MediaType,
}

/// <summary>A single change of a standardization step, for the preview.</summary>
/// <param name="Kind">What changes.</param>
/// <param name="Before">The value before (a bookpath, a manifest identifier or a media type).</param>
/// <param name="After">The value after.</param>
/// <param name="BookPath">The bookpath of the affected file once the step has run.</param>
public sealed record StandardizationChange(StandardizationChangeKind Kind, string Before, string After, string BookPath);

/// <summary>The planned changes of one step.</summary>
/// <param name="Step">The step.</param>
/// <param name="Changes">The changes, in the order they happen; empty when the step has nothing to do.</param>
public sealed record StandardizationStepPlan(StandardizationStep Step, IReadOnlyList<StandardizationChange> Changes);

/// <summary>The plan of the checked steps, in the order they run (<see cref="EpubStandardization.AllSteps"/>).</summary>
public sealed class StandardizationPlan
{
    internal StandardizationPlan(IReadOnlyList<StandardizationStepPlan> steps)
    {
        Steps = steps;
    }

    /// <summary>The plans of the checked steps.</summary>
    public IReadOnlyList<StandardizationStepPlan> Steps { get; }

    /// <summary>Whether any checked step would change something.</summary>
    public bool HasChanges => Steps.Any(s => s.Changes.Count > 0);

    /// <summary>The plan of a step, or <c>null</c> when the step is not checked.</summary>
    public StandardizationStepPlan? For(StandardizationStep step) => Steps.FirstOrDefault(s => s.Step == step);
}

/// <summary>
/// "Standardize EPUB" — the book-tidying steps that used to be separate Tools menu actions ("Restructure Epub to
/// Signet Norm", "Use Standard File Extensions", "Rebase OPF Manifest IDs on Current Filenames", "Update OPF Manifest
/// Media Types"), planned together with a preview. Each step sees the result of the steps before it, so
/// <see cref="Plan"/> runs the steps on a simulated state of the book (the bookpath of every resource and a copy of
/// the OPF document), while <see cref="Apply"/> runs them one by one on the book itself. Both use the same planning
/// code and the same OPF manifest updates (<see cref="OpfResource.RenameInDocument"/> and friends, also run by the
/// <see cref="FolderKeeper"/> rename/move handlers), so the preview shows what applying does.
/// </summary>
/// <remarks>
/// One known gap: when a renamed file's new identifier is already taken by another manifest entry, the OPF falls back
/// to a random ULID-based identifier, so the preview and the result differ in that random value.
/// </remarks>
public static class EpubStandardization
{
    /// <summary>All steps, in the order they run.</summary>
    public static IReadOnlyList<StandardizationStep> AllSteps { get; } = new[]
    {
        StandardizationStep.StandardFolders,
        StandardizationStep.StandardFileExtensions,
        StandardizationStep.RebaseManifestIds,
        StandardizationStep.ManifestMediaTypes,
    };

    /// <summary>
    /// The guard of the whole operation: the first HTML file, OPF or NCX that is not well-formed (its references could
    /// not be updated safely), or <c>null</c>.
    /// </summary>
    public static Resource? FindNotWellFormed(Book book)
    {
        ArgumentNullException.ThrowIfNull(book);
        return book.FindFirstNotWellFormedForBook();
    }

    /// <summary>Plans the given steps without changing the book.</summary>
    public static StandardizationPlan Plan(Book book, IEnumerable<StandardizationStep> steps)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(steps);
        HashSet<StandardizationStep> enabled = steps.ToHashSet();
        Simulation simulation = new(book);

        List<StandardizationStepPlan> plans = new();
        foreach (StandardizationStep step in AllSteps.Where(enabled.Contains))
        {
            IReadOnlyList<StandardizationChange> changes = step switch
            {
                StandardizationStep.StandardFolders =>
                    simulation.Relocate(PlanStandardFolders(book, simulation.Resources, simulation.Paths)),
                StandardizationStep.StandardFileExtensions =>
                    simulation.Relocate(PlanStandardFileExtensions(simulation.Resources, simulation.Paths)),
                StandardizationStep.RebaseManifestIds => simulation.RebaseManifestIds(),
                _ => simulation.ManifestMediaTypes(),
            };
            plans.Add(new StandardizationStepPlan(step, changes));
        }

        return new StandardizationPlan(plans);
    }

    /// <summary>
    /// Runs the given steps on the book, in order. Refused (nothing changes) when a file is not well-formed
    /// (<see cref="FindNotWellFormed"/>). Sets <see cref="Book.Modified"/> when anything changed.
    /// </summary>
    public static MaintenanceOperationResult Apply(Book book, IEnumerable<StandardizationStep> steps)
    {
        ArgumentNullException.ThrowIfNull(book);
        ArgumentNullException.ThrowIfNull(steps);
        if (FindNotWellFormed(book) is { } bad)
        {
            return new MaintenanceOperationResult(false, bad);
        }

        HashSet<StandardizationStep> enabled = steps.ToHashSet();
        bool changed = false;
        foreach (StandardizationStep step in AllSteps.Where(enabled.Contains))
        {
            changed |= step switch
            {
                StandardizationStep.StandardFolders => ApplyStandardFolders(book),
                StandardizationStep.StandardFileExtensions => ApplyStandardFileExtensions(book),
                StandardizationStep.RebaseManifestIds => ApplyManifestIds(book),
                _ => ApplyManifestMediaTypes(book),
            };
        }

        if (changed)
        {
            book.Modified = true;
        }

        return MaintenanceOperationResult.Ok;
    }

    /// <summary>
    /// <see cref="StandardizationStep.StandardFolders"/> on the book (no guard): renames the OPF to
    /// <c>content.opf</c> and the NCX to <c>toc.ncx</c>, resolves case-insensitive file name collisions across the
    /// whole book and moves every resource to its standard folder, updating all references. Returns whether any
    /// resource got a new bookpath.
    /// </summary>
    internal static bool ApplyStandardFolders(Book book)
    {
        IReadOnlyList<Resource> resources = book.GetAllResources();
        List<(Resource Resource, string NewBookPath)> changes =
            PlanStandardFolders(book, resources, resources.ToDictionary(r => r, r => r.BookPath));
        book.RelocateResources(changes);

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        IReadOnlyList<Resource> moved = folderKeeper.GetResourceList();
        folderKeeper.SetGroupFolders(moved.Select(r => r.BookPath).ToList(), moved.Select(r => r.MediaType).ToList());
        return changes.Count > 0;
    }

    /// <summary>
    /// <see cref="StandardizationStep.StandardFileExtensions"/> on the book (no guard). Returns whether any resource
    /// was renamed.
    /// </summary>
    internal static bool ApplyStandardFileExtensions(Book book)
    {
        IReadOnlyList<Resource> resources = book.GetAllResources();
        List<(Resource Resource, string NewBookPath)> changes =
            PlanStandardFileExtensions(resources, resources.ToDictionary(r => r, r => r.BookPath));
        book.RelocateResources(changes);
        return changes.Count > 0;
    }

    /// <summary>
    /// Splits bookpath changes the way <see cref="Book.RelocateResources"/> carries them out: first the renames (in
    /// the old folder), then the moves (to the new folder).
    /// </summary>
    internal static (List<(Resource Resource, string NewFilename)> Renames, List<(Resource Resource, string NewBookPath)> Moves)
        SplitRelocation(IReadOnlyList<(Resource Resource, string NewBookPath)> changes, Func<Resource, string> currentBookPath)
    {
        List<(Resource, string)> renames = new();
        List<(Resource, string)> moves = new();
        foreach ((Resource resource, string newBookPath) in changes)
        {
            string current = currentBookPath(resource);
            string newFilename = FileName(newBookPath);
            if (!string.Equals(FileName(current), newFilename, StringComparison.Ordinal))
            {
                renames.Add((resource, newFilename));
            }

            if (!string.Equals(Folder(current), Folder(newBookPath), StringComparison.Ordinal))
            {
                moves.Add((resource, newBookPath));
            }
        }

        return (renames, moves);
    }

    private static bool ApplyManifestIds(Book book)
    {
        OpfResource opf = book.GetOpf();
        if (opf.PlanManifestIdRebase().Count == 0)
        {
            return false;
        }

        opf.RebaseManifestIds();
        return true;
    }

    private static bool ApplyManifestMediaTypes(Book book)
    {
        OpfResource opf = book.GetOpf();
        IReadOnlyList<Resource> resources = book.GetAllResources();
        if (opf.PlanManifestMediaTypes(resources).Count == 0)
        {
            return false;
        }

        opf.UpdateManifestMediaTypes(resources);
        return true;
    }

    /// <summary>
    /// The new bookpaths of <see cref="StandardizationStep.StandardFolders"/>: <c>content.opf</c>/<c>toc.ncx</c>
    /// naming, then the case-insensitive file name collisions (the first file keeps its name, the following ones get a
    /// unique version), then the standard folder of every resource whose group has one.
    /// </summary>
    private static List<(Resource Resource, string NewBookPath)> PlanStandardFolders(
        Book book,
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<Resource, string> current)
    {
        Dictionary<Resource, string> target = new(current);
        RenameIfPresent(target, book.GetOpf(), "content.opf");
        if (book.GetNcx() is { } ncx)
        {
            RenameIfPresent(target, ncx, "toc.ncx");
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<Resource> duplicates = resources
            .Where(r => !seen.Add(FileName(target[r]).ToLowerInvariant()))
            .ToList();
        foreach (Resource resource in duplicates)
        {
            List<string> filenames = resources.Select(r => FileName(target[r])).ToList();
            string unique = FolderKeeper.UniqueFilenameVersion(FileName(target[resource]).ToLowerInvariant(), filenames);
            target[resource] = Combine(Folder(target[resource]), unique);
        }

        FolderKeeper folderKeeper = book.GetFolderKeeper();
        foreach (Resource resource in resources)
        {
            string group = MediaTypes.GetGroupFromMediaType(resource.MediaType, "other");
            if (group.Length == 0 || string.Equals(group, "other", StringComparison.Ordinal))
            {
                continue;
            }

            target[resource] = Combine(folderKeeper.GetStdFolderForGroup(group), FileName(target[resource]));
        }

        return Changed(resources, current, target);
    }

    /// <summary>
    /// The new bookpaths of <see cref="StandardizationStep.StandardFileExtensions"/>: the extension of every file
    /// whose extension does not match its media type is replaced (the folder is kept). A name already taken by another
    /// file of the same folder gets a unique version instead of overwriting it.
    /// </summary>
    private static List<(Resource Resource, string NewBookPath)> PlanStandardFileExtensions(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<Resource, string> current)
    {
        Dictionary<Resource, string> target = new(current);
        foreach (Resource resource in resources)
        {
            string extension = MediaTypes.GetExtensionFromMediaType(resource.MediaType);
            string filename = FileName(target[resource]);
            if (extension.Length == 0 || filename.EndsWith("." + extension, StringComparison.Ordinal))
            {
                continue;
            }

            string[] parts = filename.Split('.');
            string newName = parts.Length > 1
                ? string.Join('.', parts[..^1].Append(extension))
                : filename + "." + extension;
            string folder = Folder(target[resource]);
            string newBookPath = Combine(folder, newName);
            if (target.Any(p => p.Key != resource && string.Equals(p.Value, newBookPath, StringComparison.OrdinalIgnoreCase)))
            {
                newName = FolderKeeper.UniqueFilenameVersion(newName, target.Values.Select(FileName).ToList());
                newBookPath = Combine(folder, newName);
            }

            target[resource] = newBookPath;
        }

        return Changed(resources, current, target);
    }

    private static List<(Resource Resource, string NewBookPath)> Changed(
        IReadOnlyList<Resource> resources,
        IReadOnlyDictionary<Resource, string> current,
        Dictionary<Resource, string> target) =>
        resources
            .Where(r => !string.Equals(current[r], target[r], StringComparison.Ordinal))
            .Select(r => (r, target[r]))
            .ToList();

    private static void RenameIfPresent(Dictionary<Resource, string> target, Resource resource, string filename)
    {
        if (target.TryGetValue(resource, out string? bookPath) && !string.Equals(FileName(bookPath), filename, StringComparison.Ordinal))
        {
            target[resource] = Combine(Folder(bookPath), filename);
        }
    }

    private static string FileName(string bookPath) => SysPath.GetFileName(bookPath);

    private static string Folder(string bookPath) => BookPath.StartingDir(bookPath);

    private static string Combine(string folder, string filename) => folder.Length == 0 ? filename : folder + "/" + filename;

    /// <summary>
    /// The simulated state of the book for <see cref="Plan"/>: the bookpath of every resource and a copy of the OPF
    /// document, changed the same way the real book is (the <see cref="FolderKeeper"/> handlers run
    /// <see cref="OpfResource.RenameInDocument"/>, <see cref="OpfResource.MoveInDocument"/> and
    /// <see cref="OpfResource.RebaseManifestHrefs"/> on the real OPF).
    /// </summary>
    private sealed class Simulation
    {
        private readonly OpfResource _opf;
        private readonly OpfDocument _document;

        public Simulation(Book book)
        {
            _opf = book.GetOpf();
            _document = _opf.GetOpfDocument();
            Resources = book.GetAllResources();
            Paths = Resources.ToDictionary(r => r, r => r.BookPath);
        }

        public IReadOnlyList<Resource> Resources { get; }

        public Dictionary<Resource, string> Paths { get; }

        private string OpfBookPath => Paths.GetValueOrDefault(_opf, _opf.BookPath);

        /// <summary>Carries out bookpath changes like <see cref="Book.RelocateResources"/> and returns the preview rows.</summary>
        public List<StandardizationChange> Relocate(List<(Resource Resource, string NewBookPath)> changes)
        {
            Dictionary<Resource, string> before = changes.ToDictionary(c => c.Resource, c => Paths[c.Resource]);
            List<(Resource Resource, string OldId, string NewId)> idChanges = new();
            (List<(Resource Resource, string NewFilename)> renames, List<(Resource Resource, string NewBookPath)> moves) =
                SplitRelocation(changes, r => Paths[r]);

            foreach ((Resource resource, string newFilename) in renames)
            {
                string oldBookPath = Paths[resource];
                string newBookPath = Combine(Folder(oldBookPath), newFilename);
                Paths[resource] = newBookPath;
                if (ReferenceEquals(resource, _opf))
                {
                    continue;
                }

                if (OpfResource.RenameInDocument(_document, OpfBookPath, oldBookPath, newBookPath, resource.Type) is { } id
                    && !string.Equals(id.OldId, id.NewId, StringComparison.Ordinal))
                {
                    idChanges.Add((resource, id.OldId, id.NewId));
                }
            }

            foreach ((Resource resource, string newBookPath) in moves)
            {
                string oldBookPath = Paths[resource];
                Paths[resource] = newBookPath;
                if (ReferenceEquals(resource, _opf))
                {
                    OpfResource.RebaseManifestHrefs(_document, oldBookPath, newBookPath);
                }
                else
                {
                    OpfResource.MoveInDocument(_document, OpfBookPath, oldBookPath, newBookPath);
                }
            }

            return changes
                .Select(c => new StandardizationChange(StandardizationChangeKind.Path, before[c.Resource], c.NewBookPath, c.NewBookPath))
                .Concat(idChanges.Select(c => new StandardizationChange(
                    StandardizationChangeKind.ManifestId, c.OldId, c.NewId, Paths[c.Resource])))
                .ToList();
        }

        public List<StandardizationChange> RebaseManifestIds() =>
            OpfResource.PlanManifestIdRebase(_document, OpfBookPath)
                .Select(c => new StandardizationChange(StandardizationChangeKind.ManifestId, c.OldId, c.NewId, c.BookPath))
                .ToList();

        public List<StandardizationChange> ManifestMediaTypes() =>
            OpfResource.ComputeManifestMediaTypeChanges(_document, OpfBookPath, Resources, r => Paths[r])
                .Select(c => new StandardizationChange(
                    StandardizationChangeKind.MediaType, c.Change.OldMediaType, c.Change.NewMediaType, Paths[c.Change.Resource]))
                .ToList();
    }
}
