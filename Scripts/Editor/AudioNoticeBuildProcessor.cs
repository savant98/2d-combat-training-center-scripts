using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// 승인된 고지 원문만 배포한다. 내부 원장과 생성기는 검사에만 사용하고 빌드 출력으로 복사하지 않는다.
public sealed class AudioNoticeBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public const string NoticeAssetPath = "Assets/Resources/Legal/AudioNotices.txt";
    public const string RegistryPath = "tools/Audio/AudioAttribution/RequiredCredits.json";
    public const string GeneratorPath = "tools/Audio/GenerateAudioNotices.py";
    public const string ManifestPath = "tools/Audio/AudioAttribution/AudioNoticesManifest.json";
    public const string DistributionFileName = "AudioNotices.txt";

    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (SupportsTarget(report.summary.platform)) ValidateNotice();
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (!SupportsTarget(report.summary.platform)) return;
        // 일부 빌드 단계에서는 결과가 아직 Unknown일 수 있으므로 실패·취소만 제외한다.
        if (report.summary.result == BuildResult.Failed || report.summary.result == BuildResult.Cancelled) return;
        CopyNoticeForBuild(report.summary.platform, report.summary.outputPath);
    }

    public static bool SupportsTarget(BuildTarget target)
    {
        return target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64 || target == BuildTarget.WebGL;
    }

    public sealed class ValidatedNotice
    {
        public string SourcePath { get; internal set; }
        public string Sha256 { get; internal set; }
        public int EntryCount { get; internal set; }
        internal byte[] Bytes;
    }

    // 검증 도구도 콜백과 같은 복사 경로를 사용한다. projectRoot 지정은 Assets 밖 임시 fixture 검사용이다.
    public static string CopyNoticeForBuild(BuildTarget target, string outputPath, string projectRoot = null)
    {
        if (!SupportsTarget(target)) throw new BuildFailedException("음원 고지의 출력 경로를 지원하지 않는 빌드 대상입니다: " + target);
        ValidatedNotice notice = ValidateNotice(projectRoot);
        string project = ResolveProjectRoot(projectRoot);
        if (string.IsNullOrWhiteSpace(outputPath)) throw new BuildFailedException("빌드 출력 경로가 없습니다.");
        string output = Path.GetFullPath(outputPath);
        string outputDirectory;
        if (target == BuildTarget.WebGL)
        {
            outputDirectory = output;
            if (!File.Exists(Path.Combine(outputDirectory, "index.html"))) throw new BuildFailedException("WebGL 출력의 index.html을 찾지 못했습니다: " + output);
        }
        else
        {
            if (!string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(output))
                throw new BuildFailedException("Windows 빌드 실행 파일을 찾지 못했습니다: " + output);
            outputDirectory = Path.GetDirectoryName(output);
        }
        if (IsWithin(outputDirectory, Path.Combine(project, "Assets")))
            throw new BuildFailedException("음원 고지를 프로젝트 Assets 안에 배포할 수 없습니다.");
        string destination = Path.Combine(outputDirectory, DistributionFileName);
        if (string.Equals(destination, notice.SourcePath, StringComparison.OrdinalIgnoreCase))
            throw new BuildFailedException("고지 원문과 배포 목적 파일이 같습니다.");
        File.WriteAllBytes(destination, notice.Bytes);
        if (!string.Equals(Sha256(File.ReadAllBytes(destination)), notice.Sha256, StringComparison.Ordinal))
            throw new BuildFailedException("배포된 음원 고지의 SHA-256이 원문과 다릅니다: " + destination);
        return destination;
    }

    public static ValidatedNotice ValidateNotice(string projectRoot = null)
    {
        string project = ResolveProjectRoot(projectRoot);
        try
        {
            byte[] noticeBytes = ReadRequired(project, NoticeAssetPath);
            byte[] registryBytes = ReadRequired(project, RegistryPath);
            byte[] generatorBytes = ReadRequired(project, GeneratorPath);
            byte[] manifestBytes = ReadRequired(project, ManifestPath);
            var utf8 = new UTF8Encoding(false, true);
            string noticeText = utf8.GetString(noticeBytes).TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(noticeText)) Fail("음원 고지 원문이 비어 있습니다.");
            Registry registry = JsonUtility.FromJson<Registry>(utf8.GetString(registryBytes).TrimStart('\uFEFF'));
            Manifest manifest = JsonUtility.FromJson<Manifest>(utf8.GetString(manifestBytes).TrimStart('\uFEFF'));
            if (registry == null || manifest == null || registry.schemaVersion != 1 || manifest.schemaVersion != 1)
                Fail("음원 고지 원장 또는 manifest의 schemaVersion을 지원하지 않습니다.");
            if (registry.entries == null || registry.entries.Length == 0 || registry.entryCount != registry.entries.Length ||
                manifest.entries == null || manifest.entryCount != registry.entryCount || manifest.entries.Length != registry.entryCount)
                Fail("음원 고지 원장·manifest의 항목 수가 일치하지 않습니다.");
            MatchHash(manifest.noticeSha256, noticeBytes, "고지");
            MatchHash(manifest.registrySha256, registryBytes, "출처 원장");
            MatchHash(manifest.generatorSha256, generatorBytes, "고지 생성기");
            MatchRequired(registry.header, manifest.noticeHeader, "header");
            MatchRequired(registry.footer, manifest.noticeFooter, "footer");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var blocks = new List<string>();
            for (int i = 0; i < registry.entries.Length; i++)
            {
                Entry entry = registry.entries[i];
                Entry rendered = manifest.entries[i];
                if (entry == null || rendered == null) Fail("음원 고지에 빈 항목이 있습니다.");
                Required(entry.id, "id");
                if (!ids.Add(entry.id)) Fail("음원 고지 id가 중복되었습니다: " + entry.id);
                Match(entry.id, rendered.id, "id");
                MatchRequired(entry.title, rendered.title, "title");
                MatchRequired(entry.author, rendered.author, "author");
                MatchRequired(entry.sourceUrl, rendered.sourceUrl, "sourceUrl");
                MatchRequired(entry.license, rendered.license, "license");
                MatchRequired(entry.licenseName, rendered.licenseName, "licenseName");
                MatchRequired(entry.licenseVersion, rendered.licenseVersion, "licenseVersion");
                MatchRequired(entry.licenseUrl, rendered.licenseUrl, "licenseUrl");
                MatchRequired(entry.modifications, rendered.modifications, "modifications");
                if (entry.modified != rendered.modified) Fail("원장과 manifest의 modified가 다릅니다: " + entry.id);
                Match(entry.contributors, rendered.contributors, "contributors");
                Match(entry.copyrightNotice, rendered.copyrightNotice, "copyrightNotice");
                ValidateUrl(entry.sourceUrl);
                ValidateUrl(entry.licenseUrl);
                Required(rendered.noticeText, "noticeText");
                blocks.Add(rendered.noticeText);
                int start = noticeText.IndexOf(rendered.noticeText, StringComparison.Ordinal);
                if (start < 0 || noticeText.IndexOf(rendered.noticeText, start + rendered.noticeText.Length, StringComparison.Ordinal) >= 0)
                    Fail("음원 고지 본문 블록이 누락되었거나 중복되었습니다: " + entry.id);
                foreach (string required in new[] { entry.title, entry.author, entry.contributors, entry.sourceUrl, entry.licenseName,
                    entry.licenseUrl, entry.modifications, entry.copyrightNotice })
                    if (!string.IsNullOrWhiteSpace(required) && rendered.noticeText.IndexOf(required, StringComparison.Ordinal) < 0)
                        Fail("필수 표시 내용이 음원 고지 본문에 없습니다: " + entry.id);
                string[] supplied = entry.otherSuppliedNotices ?? new string[0];
                string[] renderedSupplied = rendered.otherSuppliedNotices ?? new string[0];
                if (supplied.Length != renderedSupplied.Length) Fail("추가 고지 항목 수가 다릅니다: " + entry.id);
                for (int n = 0; n < supplied.Length; n++)
                {
                    Required(supplied[n], "otherSuppliedNotices");
                    Match(supplied[n], renderedSupplied[n], "otherSuppliedNotices");
                    if (rendered.noticeText.IndexOf(supplied[n], StringComparison.Ordinal) < 0) Fail("추가 고지가 본문에 없습니다: " + entry.id);
                }
            }
            string reconstructed = manifest.noticeHeader + "\n\n" + string.Join("\n\n", blocks) + "\n\n" + manifest.noticeFooter + "\n";
            if (!string.Equals(Sha256(utf8.GetBytes(reconstructed)), Sha256(noticeBytes), StringComparison.Ordinal))
                Fail("음원 고지 원문 전체가 manifest의 고지 본문과 다릅니다.");
            // 디스크와 Unity가 실제 포함할 TextAsset의 바이트도 대조한다.
            if (string.Equals(project, ResolveProjectRoot(null), StringComparison.OrdinalIgnoreCase))
            {
                TextAsset imported = AssetDatabase.LoadAssetAtPath<TextAsset>(NoticeAssetPath);
                if (imported == null || !string.Equals(Sha256(imported.bytes), Sha256(noticeBytes), StringComparison.Ordinal))
                    Fail("Unity TextAsset이 고지 원문과 다릅니다. 고지를 다시 Import한 뒤 빌드하세요.");
            }
            return new ValidatedNotice { SourcePath = Path.Combine(project, NoticeAssetPath), Sha256 = Sha256(noticeBytes), EntryCount = registry.entryCount, Bytes = noticeBytes };
        }
        catch (BuildFailedException) { throw; }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        {
            throw new BuildFailedException("음원 고지 검사 실패: " + ex.Message);
        }
    }

    static string ResolveProjectRoot(string projectRoot)
    {
        return Path.GetFullPath(projectRoot ?? Path.GetDirectoryName(Application.dataPath)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    static bool IsWithin(string path, string directory)
    {
        string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string parent = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full.Equals(parent, StringComparison.OrdinalIgnoreCase) || full.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static byte[] ReadRequired(string project, string relativePath)
    {
        string path = Path.Combine(project, relativePath);
        if (!File.Exists(path)) Fail("음원 고지 검사 파일이 없습니다: " + relativePath);
        return File.ReadAllBytes(path);
    }

    static string Sha256(byte[] bytes)
    {
        using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }

    static void MatchHash(string expected, byte[] bytes, string label)
    {
        if (expected == null || expected.Length != 64 || !string.Equals(expected, Sha256(bytes), StringComparison.OrdinalIgnoreCase))
            Fail(label + "의 SHA-256이 manifest와 다릅니다. 고지를 다시 생성하세요.");
    }

    static void Required(string value, string label)
    {
        if (string.IsNullOrWhiteSpace(value)) Fail("음원 고지 필수 항목이 없습니다: " + label);
    }

    static void MatchRequired(string expected, string actual, string label)
    {
        Required(expected, label);
        Match(expected, actual, label);
    }

    static void Match(string expected, string actual, string label)
    {
        if (!string.Equals(expected ?? "", actual ?? "", StringComparison.Ordinal)) Fail("원장과 manifest의 음원 고지 항목이 다릅니다: " + label);
    }

    static void ValidateUrl(string value)
    {
        Uri uri;
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            Fail("음원 고지 출처 또는 라이선스 URL이 올바르지 않습니다: " + value);
    }

    static void Fail(string message) { throw new BuildFailedException(message); }

    // JsonUtility가 임시 검증 assembly에서도 읽을 수 있도록 DTO는 public 필드로 정의한다.
    [Serializable]
    public sealed class Registry
    {
        public int schemaVersion;
        public int entryCount;
        public string header;
        public string footer;
        public Entry[] entries;
    }

    [Serializable]
    public sealed class Manifest
    {
        public int schemaVersion;
        public string noticeSha256;
        public string registrySha256;
        public string generatorSha256;
        public int entryCount;
        public string noticeHeader;
        public string noticeFooter;
        public Entry[] entries;
    }

    [Serializable]
    public sealed class Entry
    {
        public string id;
        public string title;
        public string author;
        public string contributors;
        public string sourceUrl;
        public string license;
        public string licenseName;
        public string licenseVersion;
        public string licenseUrl;
        public bool modified;
        public string modifications;
        public string copyrightNotice;
        public string[] otherSuppliedNotices;
        public string noticeText;
    }
}
