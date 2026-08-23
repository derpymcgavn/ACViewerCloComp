using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

using ACE.DatLoader;
using ACE.DatLoader.FileTypes;

using DatReaderWriter;
using DatReaderWriter.Extensions;
using DatReaderWriter.Enums;
using DatReaderWriter.Options;

using AceGfxObj = ACE.DatLoader.FileTypes.GfxObj;
using AceSurface = ACE.DatLoader.FileTypes.Surface;

namespace ACViewer.Services
{
    public sealed class DatWorkspaceInfo
    {
        public string DirectoryPath { get; init; }
        public bool HasPortalDat { get; init; }
        public bool HasCellDat { get; init; }
        public bool HasHighResDat { get; init; }
        public bool HasLanguageDat { get; init; }
        public long PortalSizeBytes { get; init; }
        public string Summary => $"Portal:{Format(HasPortalDat, PortalSizeBytes)}  Cell:{Format(HasCellDat)}  HighRes:{Format(HasHighResDat)}  Language:{Format(HasLanguageDat)}";

        private static string Format(bool exists, long bytes = 0)
        {
            if (!exists) return "missing";
            if (bytes <= 0) return "ok";
            return $"{bytes / 1024d / 1024d:n1} MB";
        }
    }

    public sealed class DatWriteReceipt
    {
        public string BackupDirectory { get; init; }
        public string ManifestPath { get; init; }
        public string Message { get; init; }
    }

    public sealed class DatPackageManifest
    {
        public string Format { get; init; } = "derpace-dat-asset-package";
        public int Version { get; init; } = 1;
        public string CreatedUtc { get; init; }
        public string RootId { get; init; }
        public string SourceDatDirectory { get; init; }
        public List<DatPackageEntry> Entries { get; init; } = new List<DatPackageEntry>();
    }

    public sealed class DatPackageEntry
    {
        public string Id { get; init; }
        public string Kind { get; init; }
        public long SizeBytes { get; init; }
    }

    public sealed class DatWorkspaceService
    {
        private static readonly string[] PortalNames = { "client_portal.dat", "portal.dat" };
        private readonly IStatusSink _statusSink;

        public DatWorkspaceService(IStatusSink statusSink = null)
        {
            _statusSink = statusSink;
        }

        public DatWorkspaceInfo Inspect(string datDirectory)
        {
            if (string.IsNullOrWhiteSpace(datDirectory))
                throw new InvalidOperationException("Choose a DAT folder first.");

            var fullPath = Path.GetFullPath(datDirectory.Trim().Trim('"'));
            if (!Directory.Exists(fullPath))
                throw new DirectoryNotFoundException(fullPath);

            var portal = FindFirst(fullPath, PortalNames);
            return new DatWorkspaceInfo
            {
                DirectoryPath = fullPath,
                HasPortalDat = portal != null,
                HasCellDat = Exists(fullPath, "client_cell_1.dat", "cell.dat"),
                HasHighResDat = Exists(fullPath, "client_highres.dat", "highres.dat"),
                HasLanguageDat = Exists(fullPath, "client_local_English.dat", "local_English.dat"),
                PortalSizeBytes = portal == null ? 0 : new FileInfo(portal).Length
            };
        }

        public string ExportRenderSurface(string datDirectory, uint renderSurfaceId, string outputPath)
        {
            var info = RequirePortal(datDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? info.DirectoryPath);

            using var writer = new DatEasyWriter(info.DirectoryPath, new DatEasyWriterOptions { IncreaseIterations = false });
            var result = writer.SaveRenderSurfaceToImage(renderSurfaceId, outputPath);
            result.ThrowIfError();

            _statusSink?.Post($"Exported render surface 0x{renderSurfaceId:X8} to {outputPath}", StatusSeverity.Success);
            return outputPath;
        }

        public DatWriteReceipt ApplyRenderSurfaceImage(string datDirectory, uint renderSurfaceId, string imagePath, bool addNew, bool resizeToExisting)
        {
            var info = RequirePortal(datDirectory);
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("Image file not found.", imagePath);

            var portalPath = FindFirst(info.DirectoryPath, PortalNames);
            if (portalPath == null)
                throw new InvalidOperationException("No portal DAT found to back up.");

            var backupDir = CreateBackupDirectory();
            var portalBackup = Path.Combine(backupDir, Path.GetFileName(portalPath));
            File.Copy(portalPath, portalBackup, overwrite: false);

            using var writer = new DatEasyWriter(info.DirectoryPath, new DatEasyWriterOptions { IncreaseIterations = true });
            var result = addNew
                ? writer.AddRenderSurface(renderSurfaceId, imagePath, PixelFormat.PFID_A8R8G8B8)
                : writer.UpdateRenderSurface(renderSurfaceId, imagePath, resizeToExisting);
            result.ThrowIfError();

            var manifestPath = Path.Combine(backupDir, "manifest.json");
            var manifest = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                datDirectory = info.DirectoryPath,
                operation = addNew ? "add-render-surface" : "update-render-surface",
                id = $"0x{renderSurfaceId:X8}",
                imagePath,
                imageSha256 = Sha256(imagePath),
                backupFiles = new[] { portalBackup },
                resizeToExisting
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            var message = $"{(addNew ? "Added" : "Updated")} render surface 0x{renderSurfaceId:X8}; backup saved to {backupDir}";
            _statusSink?.Post(message, StatusSeverity.Success);
            return new DatWriteReceipt { BackupDirectory = backupDir, ManifestPath = manifestPath, Message = message };
        }

        public string ExportPortalAssetPackage(string datDirectory, uint rootId, string packagePath)
        {
            var info = RequirePortal(datDirectory);
            if (DatManager.PortalDat == null)
                throw new InvalidOperationException("Load the DATs first so package export can follow model and texture dependencies.");

            Directory.CreateDirectory(Path.GetDirectoryName(packagePath) ?? info.DirectoryPath);

            var ids = new SortedSet<uint>();
            CollectPortalDependencies(rootId, ids);
            if (ids.Count == 0)
                throw new InvalidOperationException($"No portal DAT records found for 0x{rootId:X8}.");

            if (File.Exists(packagePath)) File.Delete(packagePath);
            using var archive = ZipFile.Open(packagePath, ZipArchiveMode.Create);
            var manifest = new DatPackageManifest
            {
                CreatedUtc = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                RootId = $"0x{rootId:X8}",
                SourceDatDirectory = info.DirectoryPath
            };

            foreach (var id in ids)
            {
                var reader = DatManager.PortalDat.GetReaderForFile(id);
                if (reader?.Buffer == null || reader.Buffer.Length == 0) continue;

                var entry = archive.CreateEntry($"files/{id:X8}.bin", CompressionLevel.Optimal);
                using (var stream = entry.Open())
                    stream.Write(reader.Buffer, 0, reader.Buffer.Length);

                manifest.Entries.Add(new DatPackageEntry
                {
                    Id = $"0x{id:X8}",
                    Kind = DescribePortalId(id),
                    SizeBytes = reader.Buffer.Length
                });
            }

            var manifestEntry = archive.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open()))
                writer.Write(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            _statusSink?.Post($"Exported {manifest.Entries.Count} DAT record(s) to {packagePath}", StatusSeverity.Success);
            return packagePath;
        }

        public DatWriteReceipt ImportPortalAssetPackage(string datDirectory, string packagePath, bool allowOverwrite)
        {
            var info = RequirePortal(datDirectory);
            if (!File.Exists(packagePath))
                throw new FileNotFoundException("DAT package not found.", packagePath);

            var portalPath = FindFirst(info.DirectoryPath, PortalNames);
            if (portalPath == null)
                throw new InvalidOperationException("No portal DAT found to back up.");

            var backupDir = CreateBackupDirectory();
            var portalBackup = Path.Combine(backupDir, Path.GetFileName(portalPath));
            File.Copy(portalPath, portalBackup, overwrite: false);

            var written = new List<string>();
            using (var archive = ZipFile.OpenRead(packagePath))
            using (var dats = new DatCollection(info.DirectoryPath, DatAccessType.ReadWrite))
            {
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("files/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)))
                {
                    var idText = Path.GetFileNameWithoutExtension(entry.FullName);
                    if (!uint.TryParse(idText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var id))
                        continue;

                    if (!allowOverwrite && dats.Portal.TryGetFileBytes(id, out byte[] existing, autoDecompress: false) && existing?.Length > 0)
                        throw new InvalidOperationException($"0x{id:X8} already exists in portal.dat. Enable intentional overwrite to import this package.");

                    byte[] bytes;
                    using (var stream = entry.Open())
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        bytes = memory.ToArray();
                    }

                    var result = dats.Portal.TryWriteFileBytes(id, bytes, bytes.Length, 0);
                    result.ThrowIfError();
                    written.Add($"0x{id:X8}");
                }
            }

            var manifestPath = Path.Combine(backupDir, "manifest.json");
            var manifest = new
            {
                timestampUtc = DateTimeOffset.UtcNow,
                datDirectory = info.DirectoryPath,
                operation = "import-dat-asset-package",
                packagePath,
                packageSha256 = Sha256(packagePath),
                allowOverwrite,
                written,
                backupFiles = new[] { portalBackup }
            };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));

            var message = $"Imported {written.Count} DAT record(s); backup saved to {backupDir}";
            _statusSink?.Post(message, StatusSeverity.Success);
            return new DatWriteReceipt { BackupDirectory = backupDir, ManifestPath = manifestPath, Message = message };
        }
        public static bool TryParseHexId(string text, out uint id)
        {
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(2);
            return uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out id) && id != 0;
        }

        public static string SuggestedTextureId(uint seed = 0x06000000)
        {
            var next = seed == 0 ? 0x06000000u : seed;
            return $"0x{next:X8}";
        }

        private DatWorkspaceInfo RequirePortal(string datDirectory)
        {
            var info = Inspect(datDirectory);
            if (!info.HasPortalDat)
                throw new InvalidOperationException("This folder does not contain client_portal.dat or portal.dat.");
            return info;
        }

        private static void CollectPortalDependencies(uint id, ISet<uint> ids)
        {
            if (id == 0 || ids.Contains(id)) return;
            var reader = DatManager.PortalDat?.GetReaderForFile(id);
            if (reader?.Buffer == null || reader.Buffer.Length == 0) return;

            ids.Add(id);
            try
            {
                switch (id >> 24)
                {
                    case 0x01:
                        var gfxObj = DatManager.PortalDat.ReadFromDat<AceGfxObj>(id);
                        foreach (var surfaceId in gfxObj.Surfaces)
                            CollectPortalDependencies(surfaceId, ids);
                        break;
                    case 0x02:
                        var setup = DatManager.PortalDat.ReadFromDat<SetupModel>(id);
                        foreach (var partId in setup.Parts)
                            CollectPortalDependencies(partId, ids);
                        break;
                    case 0x05:
                        var surfaceTexture = DatManager.PortalDat.ReadFromDat<SurfaceTexture>(id);
                        foreach (var textureId in surfaceTexture.Textures)
                            CollectPortalDependencies(textureId, ids);
                        break;
                    case 0x08:
                        var surface = DatManager.PortalDat.ReadFromDat<AceSurface>(id);
                        CollectPortalDependencies(surface.OrigTextureId, ids);
                        break;
                }
            }
            catch
            {
                // Keep the package usable even when an optional reference cannot be parsed by the legacy loader.
            }
        }

        private static string DescribePortalId(uint id)
        {
            return (id >> 24) switch
            {
                0x01 => "GfxObj",
                0x02 => "SetupModel",
                0x05 => "SurfaceTexture",
                0x06 => "RenderSurface",
                0x08 => "Surface",
                0x10 => "ClothingBase",
                _ => "PortalDbObj"
            };
        }
        private static bool Exists(string directory, params string[] names) => FindFirst(directory, names) != null;

        private static string FindFirst(string directory, IEnumerable<string> names)
        {
            return names.Select(name => Path.Combine(directory, name)).FirstOrDefault(File.Exists);
        }

        private static string CreateBackupDirectory()
        {
            var root = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "DerpACE Clothing Studio", "DatBackups");
            var dir = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string Sha256(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
    }
}
