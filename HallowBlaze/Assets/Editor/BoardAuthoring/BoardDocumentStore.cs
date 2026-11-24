using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Landmarks;
using HallowBlaze.Core.Generation.Validation;
using HallowBlaze.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Distinguishes deliberate cancellation, document conflicts and I/O failure from an actual save.</summary>
    public enum BoardDocumentSaveStatus
    {
        /// <summary>The complete validated file was atomically installed and the document marked clean.</summary>
        Saved,
        /// <summary>The operation was cancelled before replacement; the document and prior target are unchanged.</summary>
        Cancelled,
        /// <summary>The draft failed the existing schema or board validation and was not written.</summary>
        Invalid,
        /// <summary>The target changed, disappeared or needs explicit overwrite consent and a fresh fingerprint.</summary>
        Conflict,
        /// <summary>The target/original is protected, outside the draft root or reached through a reparse point.</summary>
        Protected,
        /// <summary>The file transaction failed without marking the draft saved.</summary>
        IoError
    }

    /// <summary>Reports a save's exact path and failure cause without hiding errors as success.</summary>
    public sealed class BoardDocumentSaveResult
    {
        internal BoardDocumentSaveResult(BoardDocumentSaveStatus status, string path, string message)
        {
            Status = status;
            Path = path;
            Message = message;
        }

        /// <summary>Gets whether the file was saved, cancelled or rejected for a specific reason.</summary>
        public BoardDocumentSaveStatus Status { get; }
        /// <summary>Gets the selected target path; a failed path normalization retains the caller's text.</summary>
        public string Path { get; }
        /// <summary>Gets the path and actionable error cause, or an empty string for success.</summary>
        public string Message { get; }
        /// <summary>Gets true only after actual atomic installation and successful document bookkeeping.</summary>
        public bool Succeeded => Status == BoardDocumentSaveStatus.Saved;
    }

    /// <summary>Identifies one named saved board and an optional independently saved exact route assignment.</summary>
    public sealed class BoardSavedDraft
    {
        internal BoardSavedDraft(string path, string templateId, int version, BoardAssignment assignment = null, string assignmentPath = null)
        { Path = path; TemplateId = templateId; TemplateVersion = version; Assignment = assignment; AssignmentPath = assignmentPath; }
        /// <summary>Gets the author-selected file name without changing the stable template identity.</summary>
        public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
        /// <summary>Gets the template file under the selected external draft root.</summary>
        public string Path { get; }
        /// <summary>Gets the stable template identifier checked when opening an assignment.</summary>
        public string TemplateId { get; }
        /// <summary>Gets the saved template's exact content version.</summary>
        public int TemplateVersion { get; }
        /// <summary>Gets the validated route/segment/variant binding, or null for an unassigned board.</summary>
        public BoardAssignment Assignment { get; }
        /// <summary>Gets the separate binding file, or null for an unassigned board.</summary>
        public string AssignmentPath { get; }
    }

    /// <summary>Saves only external authoring drafts; never publishes, writes into the project or accesses player saves.</summary>
    public sealed class BoardDocumentStore
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        private static readonly StringComparison PathComparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        private readonly string draftRoot;
        private readonly string draftDirectory;
        private readonly string projectRoot;
        private readonly string playerRoot;
        private readonly HashSet<string> publishedPaths;
        private readonly Action<string, byte[]> writeTemporary;
        private readonly Action<string, string> replace;

        /// <summary>Creates a store fenced to one user-selected external draft directory.</summary>
        /// <param name="draftRoot">A directory outside the project and real persistent-data tree; new subdirectories may be created here only.</param>
        /// <param name="projectRoot">The actual Unity project directory, whose originals are always read-only.</param>
        /// <param name="publishedPaths">Additional immutable external publication paths, including any future active catalog references.</param>
        /// <param name="writeTemporary">Optional fault-injection operation, which must write only the supplied sibling temporary file.</param>
        /// <param name="replace">Optional fault-injection atomic install; on failure it must leave the target unchanged.</param>
        public BoardDocumentStore(string draftRoot, string projectRoot, IEnumerable<string> publishedPaths = null,
            Action<string, byte[]> writeTemporary = null, Action<string, string> replace = null)
        {
            if (!IsSupportedPlatform(Application.platform))
                throw new PlatformNotSupportedException("Authoring file storage currently requires Windows Editor filesystem identity support.");
            string selectedRoot = FullPath(draftRoot);
            draftDirectory = selectedRoot;
            this.draftRoot = PhysicalIdentity(selectedRoot);
            this.projectRoot = PhysicalIdentity(FullPath(projectRoot));
            playerRoot = PhysicalIdentity(FullPath(Application.persistentDataPath));
            if (Related(this.draftRoot, this.projectRoot) || Related(this.draftRoot, playerRoot))
                throw new ArgumentException("Draft root must be separate from project and player data.", nameof(draftRoot));
            this.publishedPaths = new HashSet<string>(PathComparison == StringComparison.OrdinalIgnoreCase
                ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string path in publishedPaths ?? Array.Empty<string>()) this.publishedPaths.Add(PhysicalIdentity(FullPath(path)));
            this.writeTemporary = writeTemporary ?? WriteDurable;
            this.replace = replace ?? Install;
        }

        /// <summary>Reports platforms with verified file-identity protection; every other platform is refused before filesystem access.</summary>
        public static bool IsSupportedPlatform(RuntimePlatform platform) => platform == RuntimePlatform.WindowsEditor;

        /// <summary>Opens an entire validated template transactionally; a protected original can only be inspected or forked.</summary>
        /// <exception cref="Exception">A read/validation error is propagated; no other document is modified.</exception>
        public BoardTemplateDocument OpenTemplate(string path, BoardValidationCatalog catalog)
        {
            string full = ReadablePath(path);
            byte[] bytes = File.ReadAllBytes(full);
            BoardTemplateDocument document = BoardTemplateDocument.ReadTemplate(Decode(bytes), catalog);
            document.MarkSaved(full, Fingerprint(bytes), IsPublished(full));
            return document;
        }

        /// <summary>Opens one independent validated assignment draft without activating or changing its template.</summary>
        public BoardTemplateDocument OpenPlacement(string path, WorldDefinition world, LandmarkTemplate template)
        {
            string full = ReadablePath(path);
            byte[] bytes = File.ReadAllBytes(full);
            BoardTemplateDocument document = BoardTemplateDocument.ReadPlacement(Decode(bytes), world, template);
            document.MarkSaved(full, Fingerprint(bytes), IsPublished(full));
            return document;
        }

        /// <summary>Opens an independent shared assignment with exact version validation and normal protected-file bookkeeping.</summary>
        public BoardTemplateDocument OpenAssignment(string path, WorldDefinition world, LandmarkTemplate template)
        {
            string full = ReadablePath(path);
            byte[] bytes = File.ReadAllBytes(full);
            var document = BoardTemplateDocument.ReadAssignment(Decode(bytes), world, template);
            document.MarkSaved(full, Fingerprint(bytes), IsPublished(full));
            return document;
        }

        /// <summary>Opens a separately saved route-source manifest through the same physical path and conflict guards as boards.</summary>
        public BoardTemplateDocument OpenPool(string path, string worldJson, IEnumerable<string> templateJsons, BoardValidationCatalog catalog)
        {
            string full = ReadablePath(path);
            byte[] bytes = File.ReadAllBytes(full);
            string json = Decode(bytes);
            JObject file = JObject.Parse(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (file["authoringSchemaVersion"] != null)
            {
                int schema = file["authoringSchemaVersion"].Type == JTokenType.Integer ? (int)file["authoringSchemaVersion"] : 0;
                if (schema != 1 && schema != 2 || file.Properties().Count() != (schema == 1 ? 4 : 5) || schema == 2 && !(file["poolVersions"] is JObject) ||
                    !(file["publication"] is JObject) || file["worldJson"]?.Type != JTokenType.String || !(file["templateJsons"] is JArray templates) ||
                    templates.Any(template => template.Type != JTokenType.String))
                    throw new InvalidDataException("Invalid or unsupported route-source archive envelope.");
                if (!JToken.DeepEquals(JObject.Parse((string)file["worldJson"]), JObject.Parse(worldJson)))
                    throw new InvalidDataException("Choose the exact captured world before opening this route publication.");
                worldJson = (string)file["worldJson"];
                templateJsons = templates.Values<string>().ToArray();
                json = file["publication"].ToString();
            }
            var document = BoardTemplateDocument.ReadPool(json, worldJson, templateJsons, catalog);
            document.RetainPoolVersions(file["poolVersions"] as JObject);
            document.MarkSaved(full, Fingerprint(bytes), IsPublished(full));
            return document;
        }

        /// <summary>Captures current target bytes for an explicit Save As overwrite preview; never grants overwrite consent by itself.</summary>
        /// <returns>A SHA-256 fingerprint, or null when the selected file does not exist.</returns>
        public string CaptureFingerprint(string path)
        {
            string full = ReadablePath(path);
            return File.Exists(full) ? Fingerprint(File.ReadAllBytes(full)) : null;
        }

        /// <summary>Resolves a readable board name to a single JSON file inside the selected draft root; IDs are unchanged.</summary>
        /// <exception cref="ArgumentException">The name is empty, too long or contains a path or invalid filename characters.</exception>
        public string NamedBoardPath(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name != name.Trim() || name.EndsWith(".", StringComparison.Ordinal) ||
                name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name == "." || name == "..")
                throw new ArgumentException("Choose a board name of 1-80 characters without a path or trailing dot/space.", nameof(name));
            string path = Path.Combine(draftDirectory, name + ".json");
            EnsureWritable(path);
            return path;
        }

        /// <summary>Resolves a separate binding filename scoped to this board name and exact world/segment/variant target.</summary>
        public string NamedAssignmentPath(string name, BoardAssignment assignment)
        {
            if (assignment == null) throw new ArgumentNullException(nameof(assignment));
            string boardPath = NamedBoardPath(name);
            string key = new JArray(assignment.WorldId, assignment.WorldVersion, assignment.EdgeId, assignment.SegmentId, assignment.VariantId).ToString(Formatting.None);
            string suffix = Fingerprint(Utf8.GetBytes(key)).Substring(0, 24);
            return Path.Combine(Path.GetDirectoryName(boardPath), name + "." + suffix + ".assignment.json");
        }

        /// <summary>Lists valid named boards and their separate bindings for the selected world without following subdirectories or publishing data.</summary>
        /// <param name="diagnostics">Files that could not be read or validated; invalid data is never silently rewritten.</param>
        public IReadOnlyList<BoardSavedDraft> ListBoards(WorldDefinition world, BoardValidationCatalog catalog, out IReadOnlyList<string> diagnostics)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var errors = new List<string>();
            var templates = new List<BoardSavedDraft>();
            var bindings = new List<string>();
            foreach (string path in Directory.GetFiles(ReadablePath(draftDirectory), "*.json", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    string json = Decode(File.ReadAllBytes(ReadablePath(path)));
                    JObject data = JObject.Parse(json);
                    if (data["variants"] != null)
                    {
                        var template = OpenTemplate(path, catalog);
                        if (template.Diagnostics.Count > 0) throw new InvalidDataException(template.Diagnostics[0].Message);
                        templates.Add(new BoardSavedDraft(path, (string)data["templateId"], (int)data["templateVersion"]));
                    }
                    else if (data["segmentId"] != null && data["templateId"] != null) bindings.Add(path);
                }
                catch (Exception exception) { errors.Add(Path.GetFileName(path) + ": " + exception.Message); }
            }
            var saved = new List<BoardSavedDraft>();
            foreach (var template in templates.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                int count = saved.Count;
                foreach (string path in bindings)
                {
                    try
                    {
                        JObject identity = JObject.Parse(Decode(File.ReadAllBytes(ReadablePath(path))));
                        if ((string)identity["templateId"] != template.TemplateId || (int?)identity["templateVersion"] != template.TemplateVersion) continue;
                        var layout = BoardTemplateJson.ReadCompatible(OpenTemplate(template.Path, catalog).ToJson(), catalog);
                        var binding = OpenAssignment(path, world, layout);
                        var assignment = BoardAssignment.Read(binding.ToJson(), world, layout);
                        saved.Add(new BoardSavedDraft(template.Path, template.TemplateId, template.TemplateVersion, assignment, path));
                    }
                    catch (Exception exception) { errors.Add(Path.GetFileName(path) + ": " + exception.Message); }
                }
                if (saved.Count == count) saved.Add(template);
            }
            diagnostics = errors.AsReadOnly();
            return saved.AsReadOnly();
        }

        /// <summary>Validates, durably stages, rechecks external changes, then atomically installs one draft file.</summary>
        /// <param name="document">The sole draft to save; other documents and runtime data are never written.</param>
        /// <param name="path">Save As destination; null uses the last opened/saved path. IDs are not rewritten.</param>
        /// <param name="confirmOverwrite">Explicit consent for replacing an existing Save As target.</param>
        /// <param name="expectedTargetFingerprint">Fingerprint shown in the overwrite preview; required with consent for an existing Save As target.</param>
        /// <param name="cancellationToken">Cancellation is honored until installation; once installed, the result is Saved.</param>
        /// <returns>A typed result; failure preserves document path, saved fingerprint, dirty state and undo history.</returns>
        public BoardDocumentSaveResult Save(BoardTemplateDocument document, string path = null, bool confirmOverwrite = false,
            string expectedTargetFingerprint = null, CancellationToken cancellationToken = default)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            string target = path ?? document.SourcePath;
            string temporary = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (document.IsReadOnly) return Result(BoardDocumentSaveStatus.Protected, target, "Published original is read-only; fork a new identity first.");
                target = FullPath(target);
                EnsureWritable(target);
                bool samePath = string.Equals(target, document.SourcePath, PathComparison);
                string expected = samePath ? document.FileFingerprint : expectedTargetFingerprint;
                if (!samePath && File.Exists(target) && (!confirmOverwrite || expected == null))
                    return Result(BoardDocumentSaveStatus.Conflict, target, "Save As requires explicit overwrite consent and the previewed fingerprint.");
                if (!Matches(target, expected))
                    return Result(BoardDocumentSaveStatus.Conflict, target, "Target changed or disappeared; Reload, Save As or Cancel.");
                if (document.Diagnostics.Count > 0)
                    return Result(BoardDocumentSaveStatus.Invalid, target, document.Diagnostics[0].Message);
                string json = document.IsPool ? document.ToPoolFileJson() : document.ToJson();
                string snapshot = BoardAuthoringJson.Format(document.Snapshot());
                byte[] bytes = Utf8.GetBytes(json);
                string fingerprint = Fingerprint(bytes);
                cancellationToken.ThrowIfCancellationRequested();
                string directory = Path.GetDirectoryName(target);
                Directory.CreateDirectory(directory);
                EnsureWritable(target);
                temporary = Path.Combine(directory, "." + Path.GetFileName(target) + "." + Guid.NewGuid().ToString("N") + ".tmp");
                writeTemporary(temporary, bytes);
                byte[] staged = File.ReadAllBytes(temporary);
                if (Fingerprint(staged) != fingerprint) throw new IOException("Staged bytes differ from the validated document.");
                cancellationToken.ThrowIfCancellationRequested();
                EnsureWritable(target);
                if (!Matches(target, expected) || snapshot != BoardAuthoringJson.Format(document.Snapshot()))
                    return Result(BoardDocumentSaveStatus.Conflict, target, "Target or draft changed while staging; nothing was installed.");
                replace(temporary, target);
                temporary = null;
                document.MarkSaved(target, fingerprint, false);
                return Result(BoardDocumentSaveStatus.Saved, target, string.Empty);
            }
            catch (OperationCanceledException)
            {
                return Result(BoardDocumentSaveStatus.Cancelled, target, "Save cancelled before installation.");
            }
            catch (SecurityException exception)
            {
                return Result(BoardDocumentSaveStatus.Protected, target, exception.Message);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is NotSupportedException)
            {
                return Result(BoardDocumentSaveStatus.IoError, target, exception.Message);
            }
            finally
            {
                if (temporary != null)
                {
                    try { File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }

        private string ReadablePath(string path)
        {
            string full = FullPath(path);
            string identity = PhysicalIdentity(full);
            if (Within(identity, playerRoot) || !Within(identity, draftRoot) && !Within(identity, projectRoot) && !publishedPaths.Contains(identity))
                throw new SecurityException("Read is outside authoring roots or would access player data: " + full);
            return full;
        }

        private void EnsureWritable(string path)
        {
            string identity = PhysicalIdentity(path);
            if (!Within(identity, draftRoot) || IsPublishedIdentity(identity) || Within(identity, playerRoot) ||
                !string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Only unpublished .json drafts inside the selected external root may be written: " + path);
        }

        private bool IsPublished(string path) => IsPublishedIdentity(PhysicalIdentity(path));

        private bool IsPublishedIdentity(string identity) => Within(identity, projectRoot) || publishedPaths.Contains(identity);

        private static bool Within(string path, string root) => string.Equals(path, root, PathComparison) ||
            path.StartsWith(root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + Path.DirectorySeparatorChar, PathComparison);

        private static bool Related(string left, string right) => Within(left, right) || Within(right, left);

        private static string FullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An explicit authoring path is required.", nameof(path));
            RejectDevicePath(path);
            string full = Path.GetFullPath(path);
            RejectDevicePath(full);
            return full.Length > Path.GetPathRoot(full).Length ? full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : full;
        }

        private static void RejectDevicePath(string path)
        {
            string normalized = path.Replace('/', '\\');
            if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                normalized.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                normalized.StartsWith(@"\??\", StringComparison.Ordinal) ||
                normalized.StartsWith(@"\\??\", StringComparison.Ordinal))
                throw new SecurityException("Device and extended-length paths are not supported for authoring: " + path);
        }

        private static string PhysicalIdentity(string path)
        {
            if (Path.DirectorySeparatorChar != '\\')
                throw new PlatformNotSupportedException("Authoring filesystem identity is implemented only for Windows Editor.");
            string root = Path.GetPathRoot(path);
            if (root.Length != 3 || root[1] != ':')
                throw new SecurityException("Authoring requires a resolvable local Windows volume: " + path);
            var device = new StringBuilder(32768);
            if (QueryDosDevice(root.Substring(0, 2), device, device.Capacity) == 0)
                throw new SecurityException("Cannot resolve Windows volume identity for authoring: " + path);
            string volume = device.ToString().Split('\0')[0];
            if (!Regex.IsMatch(volume, @"^\\Device\\HarddiskVolume[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new SecurityException("SUBST, network and ambiguous drive mappings are not supported for authoring: " + path);
            RejectReparsePoints(path);
            var missing = new Stack<string>();
            string existing = path;
            while (!File.Exists(existing) && !Directory.Exists(existing))
            {
                missing.Push(Path.GetFileName(existing));
                existing = Path.GetDirectoryName(existing);
                if (string.IsNullOrEmpty(existing)) throw new SecurityException("Cannot resolve an existing authoring parent: " + path);
            }
            using (SafeFileHandle handle = CreateFile(existing, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (handle.IsInvalid) throw new SecurityException("Cannot resolve the physical authoring path: " + path);
                var resolved = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, resolved, (uint)resolved.Capacity, 1);
                if (length == 0 || length >= resolved.Capacity ||
                    !resolved.ToString().StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase))
                    throw new SecurityException("Cannot establish a local volume/file identity: " + path);
                string identity = resolved.ToString().TrimEnd('\\');
                while (missing.Count > 0) identity += "\\" + missing.Pop();
                return identity;
            }
        }

        [DllImport("kernel32.dll", EntryPoint = "QueryDosDeviceW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern uint QueryDosDevice(string deviceName, StringBuilder targetPath, int maximumLength);

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFile(string fileName, uint access, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flags, IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint length, uint flags);

        private static void RejectReparsePoints(string path)
        {
            for (string current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
                if ((Directory.Exists(current) || File.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new SecurityException("Authoring paths may not traverse symbolic links or reparse points: " + current);
        }

        private static bool Matches(string path, string expected) => File.Exists(path)
            ? expected != null && Fingerprint(File.ReadAllBytes(path)) == expected : expected == null;

        private static string Decode(byte[] bytes)
        {
            string text = Utf8.GetString(bytes);
            return text.Length > 0 && text[0] == '\uFEFF' ? text.Substring(1) : text;
        }

        private static string Fingerprint(byte[] bytes)
        {
            using (SHA256 algorithm = SHA256.Create()) return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", string.Empty);
        }

        private static void WriteDurable(string path, byte[] bytes)
        {
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
        }

        private static void Install(string temporary, string target)
        {
            if (File.Exists(target)) File.Replace(temporary, target, null);
            else File.Move(temporary, target);
        }

        private static BoardDocumentSaveResult Result(BoardDocumentSaveStatus status, string path, string message) =>
            new BoardDocumentSaveResult(status, path, string.IsNullOrEmpty(message) ? string.Empty : path + ": " + message);
    }
}