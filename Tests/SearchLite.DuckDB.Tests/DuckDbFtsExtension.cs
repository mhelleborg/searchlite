using System.IO.Compression;
using DuckDB.NET.Data;

namespace SearchLite.Tests.DuckDB;

/// <summary>
/// Provides a directory containing the DuckDB <c>fts</c> extension, suitable for passing as the
/// <c>extension_directory</c> to <see cref="global::SearchLite.DuckDB.SearchManager"/>.
/// </summary>
/// <remarks>
/// The build/CI environment used for SearchLite does not have outbound access to DuckDB's extension
/// repository (extensions.duckdb.org), so the test suite cannot rely on DuckDB's own
/// <c>INSTALL fts</c>. The official, byte-identical extension binary is published as a Python wheel on
/// PyPI (<c>duckdb-extension-fts</c>); this helper fetches the wheel matching the loaded DuckDB
/// version/platform once and lays it out in the directory structure DuckDB expects
/// (<c>&lt;dir&gt;/&lt;version&gt;/&lt;platform&gt;/fts.duckdb_extension</c>), so the test then just loads it
/// from disk. The download is skipped entirely when the binary is already present.
/// </remarks>
public static class DuckDbFtsExtension
{
    private static readonly Lazy<string> Directory = new(Provision);

    public static string EnsureAvailable() => Directory.Value;

    private static string Provision()
    {
        var (version, platform) = GetDuckDbInfo();
        var root = Path.Combine(Path.GetTempPath(), "searchlite-duckdb-ext");
        var targetDir = Path.Combine(root, version, platform);
        var targetFile = Path.Combine(targetDir, "fts.duckdb_extension");

        if (File.Exists(targetFile))
        {
            return root;
        }

        // First, try DuckDB's own install path (works when the environment has network access to the
        // extension repository); fall back to the PyPI wheel otherwise.
        if (TryInstallViaDuckDb(root))
        {
            return root;
        }

        System.IO.Directory.CreateDirectory(targetDir);
        DownloadFromPyPi(version, platform, targetFile);
        return root;
    }

    private static bool TryInstallViaDuckDb(string extensionDirectory)
    {
        try
        {
            using var conn = new DuckDBConnection("DataSource=:memory:");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SET extension_directory='{extensionDirectory.Replace("'", "''")}'; INSTALL fts; LOAD fts;";
            cmd.ExecuteNonQuery();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static (string version, string platform) GetDuckDbInfo()
    {
        using var conn = new DuckDBConnection("DataSource=:memory:");
        conn.Open();

        // library_version is e.g. "v1.5.3"; PRAGMA platform is e.g. "linux_amd64".
        using var versionCmd = conn.CreateCommand();
        versionCmd.CommandText = "SELECT library_version FROM pragma_version();";
        var version = (string)versionCmd.ExecuteScalar()!;

        using var platformCmd = conn.CreateCommand();
        platformCmd.CommandText = "PRAGMA platform;";
        var platform = (string)platformCmd.ExecuteScalar()!;

        return (version, platform);
    }

    private static void DownloadFromPyPi(string version, string platform, string targetFile)
    {
        // DuckDB version "v1.5.3" -> wheel version "1.5.3".
        var wheelVersion = version.TrimStart('v');
        // DuckDB platform "linux_amd64" -> PyPI wheel platform tag "manylinux2014_x86_64" (and friends).
        var wheelPlatform = MapPlatform(platform);

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("searchlite-tests/1.0");

        var index = http.GetStringAsync("https://pypi.org/simple/duckdb-extension-fts/").GetAwaiter().GetResult();

        var wheelUrl = FindWheelUrl(index, wheelVersion, wheelPlatform)
                       ?? throw new InvalidOperationException(
                           $"Could not locate a duckdb-extension-fts wheel for version {wheelVersion} platform {wheelPlatform}.");

        var bytes = http.GetByteArrayAsync(wheelUrl).GetAwaiter().GetResult();
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("/fts.duckdb_extension", StringComparison.Ordinal)
                                                    || e.FullName.EndsWith("fts.duckdb_extension", StringComparison.Ordinal))
                    ?? throw new InvalidOperationException("fts.duckdb_extension not found in the downloaded wheel.");

        using var entryStream = entry.Open();
        using var output = File.Create(targetFile);
        entryStream.CopyTo(output);
    }

    private static string[] MapPlatform(string duckPlatform) => duckPlatform switch
    {
        "linux_amd64" => ["manylinux2014_x86_64", "manylinux_2_17_x86_64", "linux_x86_64"],
        "linux_arm64" => ["manylinux2014_aarch64", "manylinux_2_17_aarch64", "linux_aarch64"],
        "osx_amd64" => ["macosx_10_9_x86_64", "macosx_11_0_x86_64"],
        "osx_arm64" => ["macosx_11_0_arm64", "macosx_12_0_arm64"],
        "windows_amd64" => ["win_amd64"],
        _ => [duckPlatform]
    };

    private static string? FindWheelUrl(string simpleIndexHtml, string version, string[] platformTags)
    {
        foreach (var line in simpleIndexHtml.Split('\n'))
        {
            var hrefIdx = line.IndexOf("href=\"", StringComparison.Ordinal);
            if (hrefIdx < 0) continue;
            var start = hrefIdx + "href=\"".Length;
            var end = line.IndexOf('"', start);
            if (end < 0) continue;

            var url = line.Substring(start, end - start);
            var fileName = url.Split('/').Last().Split('#')[0];
            if (!fileName.Contains($"-{version}-", StringComparison.Ordinal)) continue;
            if (platformTags.Any(tag => fileName.Contains(tag, StringComparison.Ordinal)))
            {
                return url;
            }
        }

        return null;
    }
}
