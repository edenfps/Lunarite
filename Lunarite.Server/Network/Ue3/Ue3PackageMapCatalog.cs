using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Lunarite.Resources;

namespace Lunarite.Network.Ue3;

// Mirrors UE3 UPackageMap::Compute/IndexToObject. UELib is loaded by reflection when present so
// the production server remains buildable from a clean clone where analysis-only UELib is ignored.
internal sealed class Ue3PackageMapCatalog
{
    internal sealed record PackageUse(string Name, string FileName, string Guid, int Generation);
    internal sealed record ObjectRef(string Package, int ExportIndex, string Path, string ClassName);
    private sealed record LoadedUse(PackageUse Use, IList Exports, IList Names,
        int ObjectCount, int NameCount);

    private readonly string _clientRoot;
    private readonly List<PackageUse> _uses = [];
    private readonly List<LoadedUse> _loaded = [];
    private Dictionary<string, string[]>? _files;
    private MethodInfo? _loadPackage;
    private bool _complete = true;

    public Ue3PackageMapCatalog(string? clientRoot = null) =>
        _clientRoot = clientRoot ?? ClientFolder.Root;

    internal int MaxObjectIndex => _complete ? _loaded.Sum(item => item.ObjectCount) : 0;
    internal int MaxNameIndex => _complete ? _loaded.Sum(item => item.NameCount) : 0;

    internal void Reset()
    {
        _uses.Clear();
        _loaded.Clear();
        _complete = true;
    }

    internal bool AddUse(PackageUse use, out string error)
    {
        error = "";
        if (_uses.Any(item => item.Guid.Equals(use.Guid, StringComparison.OrdinalIgnoreCase)))
            return true;
        _uses.Add(use);
        try
        {
            string? path = FindPackage(use);
            MethodInfo? loader = GetLoader();
            if (path is null || loader is null)
                throw new FileNotFoundException(path is null
                    ? $"package not found: {use.Name} ({use.FileName})"
                    : "Eliot.UELib.dll not found");

            object package = loader.Invoke(null, [path, FileAccess.Read])!;
            object summary = package.GetType().GetField("Summary")!.GetValue(package)!;
            int objectCount = (int)summary.GetType().GetField("ExportCount")!.GetValue(summary)!;
            int nameCount = (int)summary.GetType().GetField("NameCount")!.GetValue(summary)!;
            object generations = summary.GetType().GetField("Generations")!.GetValue(summary)!;
            int localGenerations = (int)generations.GetType().GetProperty("Count")!.GetValue(generations)!;
            if (use.Generation > 0 && use.Generation < localGenerations)
            {
                object generation = generations.GetType().GetProperty("Item")!
                    .GetValue(generations, [use.Generation - 1])!;
                int generationExports = (int)generation.GetType().GetProperty("ExportCount")!
                    .GetValue(generation)!;
                int generationNames = (int)generation.GetType().GetProperty("NameCount")!
                    .GetValue(generation)!;
                objectCount = Math.Min(objectCount, generationExports);
                nameCount = Math.Min(nameCount, generationNames);
            }
            var exports = (IList)package.GetType().GetProperty("Exports")!.GetValue(package)!;
            var names = (IList)package.GetType().GetProperty("Names")!.GetValue(package)!;
            _loaded.Add(new LoadedUse(use, exports, names, objectCount, nameCount));
            return true;
        }
        catch (Exception ex)
        {
            _complete = false;
            error = $"{use.Name}: {(ex.InnerException ?? ex).Message}";
            return false;
        }
    }

    internal ObjectRef? Resolve(int staticIndex)
    {
        if (!_complete || staticIndex < 0) return null;
        foreach (LoadedUse item in _loaded)
        {
            if (staticIndex < item.ObjectCount)
            {
                if (staticIndex >= item.Exports.Count) return null;
                object export = item.Exports[staticIndex]!;
                string path = export.GetType().GetMethod("GetPath")!.Invoke(export, null)?.ToString() ?? "";
                object? exportClass = export.GetType().GetProperty("Class")?.GetValue(export);
                string className = exportClass?.GetType().GetProperty("ObjectName")?.GetValue(exportClass)?.ToString()
                    ?? export.GetType().GetProperty("ClassName")?.GetValue(export)?.ToString() ?? "";
                return new ObjectRef(item.Use.Name, staticIndex, path, className);
            }
            staticIndex -= item.ObjectCount;
        }
        return null;
    }

    internal string? ResolveName(int staticIndex)
    {
        if (!_complete || staticIndex < 0) return null;
        foreach (LoadedUse item in _loaded)
        {
            if (staticIndex < item.NameCount)
            {
                if (staticIndex >= item.Names.Count) return null;
                object name = item.Names[staticIndex]!;
                return name.GetType().GetProperty("Name")?.GetValue(name)?.ToString();
            }
            staticIndex -= item.NameCount;
        }
        return null;
    }

    private MethodInfo? GetLoader()
    {
        if (_loadPackage is not null) return _loadPackage;
        string repository = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        string[] candidates =
        [
            Path.Combine(AppContext.BaseDirectory, "Eliot.UELib.dll"),
            Path.Combine(repository, "UELib", "src", "bin", "Debug", "netstandard2.1", "Eliot.UELib.dll"),
            Path.Combine(repository, "UELib", "src", "bin", "Release", "netstandard2.1", "Eliot.UELib.dll")
        ];
        string? dll = candidates.FirstOrDefault(File.Exists);
        if (dll is null) return null;
        Assembly assembly = Assembly.LoadFrom(dll);
        Type loader = assembly.GetType("UELib.UnrealLoader")!;
        return _loadPackage = loader.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(method => method.Name == "LoadPackage" && method.GetParameters() is var p
                && p.Length == 2 && p[0].ParameterType == typeof(string)
                && p[1].ParameterType == typeof(FileAccess));
    }

    private string? FindPackage(PackageUse use)
    {
        _files ??= Directory.EnumerateFiles(_clientRoot, "*", SearchOption.AllDirectories)
            .Where(path => new[] { ".u", ".upk", ".umap" }.Contains(
                Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .GroupBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        string requested = Path.GetFileName(use.FileName);
        if (requested.Length > 0 && _files.TryGetValue(requested, out string[]? exact))
            return exact.Length == 1 ? exact[0] : exact.FirstOrDefault(path =>
                Path.GetFileNameWithoutExtension(path).Equals(use.Name, StringComparison.OrdinalIgnoreCase));
        foreach (string extension in new[] { ".upk", ".umap", ".u" })
            if (_files.TryGetValue(use.Name + extension, out string[]? candidates))
                return candidates.Length == 1 ? candidates[0] : null;
        return null;
    }
}
