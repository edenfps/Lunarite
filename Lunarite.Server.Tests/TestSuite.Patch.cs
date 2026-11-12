using Microsoft.Extensions.Logging;

using Lunarite.Network;
using Lunarite.World;

internal sealed partial class TestSuite
{
    private void PatchManifestAndCrc()
    {
        _patchRoot = Path.Combine(Path.GetTempPath(), $"huxley-patch-{Environment.ProcessId}");
        Directory.CreateDirectory(Path.Combine(_patchRoot, "Config"));
        File.WriteAllText(Path.Combine(_patchRoot, "Config", "Game.ini"), "[HuxleyGame.HuxleySubsystem]\r\nm_strMMOIP=10.101.156.3\r\nm_nPort=1\r\nm_strShopServerIP=127.0.0.1\r\n");
        File.WriteAllBytes(Path.Combine(_patchRoot, "Map.war"), [1, 2, 3]);
        string patchManifest = Path.Combine(_patchRoot, "manifest.txt");
        File.WriteAllText(patchManifest, "# comment\nConfig/Game.ini\n\\Map.war\n..\\escape.txt\nmap.war\n");
        var patchFiles = PatchServer.ReadManifest(patchManifest);
        var patchServer = new PatchServer(_patchRoot, patchFiles, Path.Combine(_patchRoot, "version.txt"),
            _loggerFactory.CreateLogger<PatchServer>());
        string patchVersion = patchServer.CurrentVersion();
        File.WriteAllBytes(Path.Combine(_patchRoot, "Map.war"), [1, 2, 3]);
        string sameContentVersion = patchServer.CurrentVersion();
        File.WriteAllBytes(Path.Combine(_patchRoot, "Map.war"), [1, 2, 4]);
        // The CRC cache is keyed by size and write time; the clock tick can be coarser than this test.
        File.SetLastWriteTimeUtc(Path.Combine(_patchRoot, "Map.war"), DateTime.UtcNow.AddMinutes(1));
        string changedVersion = patchServer.CurrentVersion();
        byte[] renderedIni = patchServer.RenderConfig(@"Config\Game.ini", "203.0.113.10")!;
        Check(patchFiles.SequenceEqual([@"Config\Game.ini", "Map.war"])
            && patchVersion.Length == 14 && string.CompareOrdinal(patchVersion, PatchServer.BaseVersion) > 0
            && sameContentVersion == patchVersion && string.CompareOrdinal(changedVersion, patchVersion) > 0
            && patchServer.VersionFile() == $"00000012 {changedVersion} Patch/00000012_{changedVersion}.rtp 0\r\n{changedVersion} {changedVersion} none 0\r\n"
            && System.Text.Encoding.ASCII.GetString(renderedIni) == "[HuxleyGame.HuxleySubsystem]\r\nm_strMMOIP=203.0.113.10\r\nm_nPort=1\r\nm_strShopServerIP=203.0.113.10\r\n"
            && patchServer.FullFileList("203.0.113.10") ==
                $"\"Config\\Game.ini\" {renderedIni.Length} {PatchServer.Crc32(renderedIni):X}\r\n\"Map.war\" 3 {PatchServer.Crc32(new byte[] { 1, 2, 4 }):X}\r\n"
            && PatchServer.Crc32("123456789"u8) == 0xCBF43926,
            "launcher patch server: manifest paths, content-based versions, per-address MMO IP and FullFile.txt sizes/CRCs");
    }

    private void PatchBaseClient()
    {
        string root = Path.Combine(Path.GetTempPath(), $"huxley-patch-base-{Environment.ProcessId}");
        string payload = Path.Combine(root, "payload"), clean = Path.Combine(root, "clean");
        foreach (string dir in new[] { payload, Path.Combine(clean, "Binaries"), Path.Combine(clean, "HuxleyMMOGame", "Logs"), Path.Combine(clean, "HuxleyMMOGame", "Config") })
            Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(payload, "Map.war"), [9, 9]);
        File.WriteAllBytes(Path.Combine(clean, "Map.war"), [1]);
        File.WriteAllBytes(Path.Combine(clean, "Binaries", "Game.exe"), [7, 7, 7]);
        foreach (string junk in new[] { "list.txt", "files.txt", "Has Space.txt", "한.txt", @"Binaries\Game.exe - Shortcut.lnk",
                     @"Binaries\Run.log", @"HuxleyMMOGame\Logs\Launch.txt",
                     @"HuxleyMMOGame\Config\HuxleyMMOEngine.ini" })
            File.WriteAllText(Path.Combine(clean, junk), "x");
        var server = new PatchServer(payload, ["Map.war"], Path.Combine(root, "version.txt"),
            _loggerFactory.CreateLogger<PatchServer>(), clean);
        string version = server.CurrentVersion();
        File.WriteAllBytes(Path.Combine(clean, "Binaries", "Game.exe"), [7, 7, 8]);
        File.SetLastWriteTimeUtc(Path.Combine(clean, "Binaries", "Game.exe"), DateTime.UtcNow.AddMinutes(1));
        string expected = $"\"Map.war\" 2 {PatchServer.Crc32(new byte[] { 9, 9 }):X}\r\n\"Binaries\\Game.exe\" 3 {PatchServer.Crc32(new byte[] { 7, 7, 8 }):X}\r\n";
        string changed = server.CurrentVersion();
        Check(server.FullFileList("127.0.0.1") == expected && string.CompareOrdinal(changed, version) > 0,
            "patch server with a base client: payload files win, every other clean file is listed, junk and unrequestable paths are skipped, base changes bump the version");
        Check(PatchServer.IsGeneratedConfig(@"HuxleyMMOGame\Config\HuxleyMMOInput.ini")
            && PatchServer.IsGeneratedConfig(@"huxleymmogame\config\HuxleyMMOUI.ini")
            && !PatchServer.IsGeneratedConfig(@"HuxleyMMOGame\Config\DefaultInput.ini")
            && !PatchServer.IsGeneratedConfig(@"HuxleyGame\Config\DefaultEngine.ini"),
            "the game-written HuxleyMMO*.ini settings files are never listed, so updates keep player settings");

        // A restarted server reads the CRCs back instead of the files: a cached entry with the
        // file's size and write time is trusted, so a planted wrong CRC shows through.
        string cache = Path.Combine(root, "version.txt.crc");
        string gameExe = Path.GetFullPath(Path.Combine(clean, "Binaries", "Game.exe"));
        string[] lines = File.ReadAllLines(cache);
        File.WriteAllLines(cache, lines.Select(line => line.StartsWith(gameExe + "\t", StringComparison.Ordinal)
            ? string.Join('\t', line.Split('\t')[..3]) + "\tDEADBEEF" : line));
        var restarted = new PatchServer(payload, ["Map.war"], Path.Combine(root, "version.txt"),
            _loggerFactory.CreateLogger<PatchServer>(), clean);
        Check(lines.Length == 2 && restarted.FullFileList("127.0.0.1").Contains("\"Binaries\\Game.exe\" 3 DEADBEEF\r\n"),
            "patch server keeps file CRCs in <version file>.crc across restarts");

        // A payload push that adds a file: Reload re-reads the manifest, no restart.
        string reloadManifest = Path.Combine(root, "reload-manifest.txt");
        File.WriteAllText(reloadManifest, "Map.war\n");
        var reloading = new PatchServer(payload, PatchServer.ReadManifest(reloadManifest), Path.Combine(root, "version-reload.txt"),
            _loggerFactory.CreateLogger<PatchServer>());
        reloading.UseManifest(reloadManifest);
        string beforeReload = reloading.CurrentVersion();
        File.WriteAllBytes(Path.Combine(payload, "New.u"), [5, 5, 5]);
        File.WriteAllText(reloadManifest, "Map.war\nNew.u\n");
        bool listedBefore = reloading.FullFileList("127.0.0.1").Contains("New.u");
        bool reloaded = reloading.Reload();
        Check(!listedBefore && reloaded && reloading.FullFileList("127.0.0.1").Contains("\"New.u\" 3 ")
            && string.CompareOrdinal(reloading.CurrentVersion(), beforeReload) > 0,
            "patch server reload picks up a file added to the manifest and bumps the version without a restart");
        File.Delete(Path.Combine(payload, "New.u"));

        // patch/korean.txt: Korean launchers get the clean client's Korean text and skin; English
        // ones keep the payload's KOR file and never get the Korean-only skin.
        Directory.CreateDirectory(Path.Combine(payload, "Loc", "KOR"));
        Directory.CreateDirectory(Path.Combine(clean, "Loc", "KOR"));
        Directory.CreateDirectory(Path.Combine(clean, "UI"));
        File.WriteAllBytes(Path.Combine(payload, "Loc", "KOR", "Items.kor"), [1]);
        File.WriteAllBytes(Path.Combine(clean, "Loc", "KOR", "Items.kor"), [2, 2]);
        File.WriteAllBytes(Path.Combine(clean, "UI", "Skin_kor.upk"), [3, 3, 3]);
        var bilingual = new PatchServer(payload, ["Map.war", @"Loc\KOR\Items.kor"], Path.Combine(root, "version2.txt"),
            _loggerFactory.CreateLogger<PatchServer>(), clean, [@"Loc\KOR\", @"UI\Skin_kor.upk"]);
        string english = bilingual.FullFileList("127.0.0.1"), korean = bilingual.FullFileList("127.0.0.1", korean: true);
        Check(english.Contains($"\"Loc\\KOR\\Items.kor\" 1 {PatchServer.Crc32(new byte[] { 1 }):X}\r\n") && !english.Contains("Skin_kor")
            && korean.Contains($"\"Loc\\KOR\\Items.kor\" 2 {PatchServer.Crc32(new byte[] { 2, 2 }):X}\r\n")
            && korean.Contains($"\"UI\\Skin_kor.upk\" 3 {PatchServer.Crc32(new byte[] { 3, 3, 3 }):X}\r\n")
            && PatchServer.IsKorean("ko-KR,ko;q=0.9") && PatchServer.IsKorean(" ko") && !PatchServer.IsKorean("en")
            && !PatchServer.IsKorean(null),
            "patch server gives Korean launchers the clean client's Korean files (patch/korean.txt) and English ones the payload's");

        // HUXLEY_PATCH_KOREAN_ROOT: Korean launchers get the merged copy of a Korean file; English ones never do.
        string koreanRoot = Path.Combine(root, "ko");
        Directory.CreateDirectory(Path.Combine(koreanRoot, "Loc", "KOR"));
        File.WriteAllBytes(Path.Combine(koreanRoot, "Loc", "KOR", "Items.kor"), [6, 6, 6, 6, 6, 6]);
        var merged = new PatchServer(payload, ["Map.war", @"Loc\KOR\Items.kor"], Path.Combine(root, "version4.txt"),
            _loggerFactory.CreateLogger<PatchServer>(), clean, [@"Loc\KOR\", @"UI\Skin_kor.upk"], null, koreanRoot);
        Check(merged.FullFileList("127.0.0.1", korean: true)
                .Contains($"\"Loc\\KOR\\Items.kor\" 6 {PatchServer.Crc32(new byte[] { 6, 6, 6, 6, 6, 6 }):X}\r\n")
            && merged.FullFileList("127.0.0.1").Contains($"\"Loc\\KOR\\Items.kor\" 1 {PatchServer.Crc32(new byte[] { 1 }):X}\r\n"),
            "patch server gives Korean launchers the HUXLEY_PATCH_KOREAN_ROOT merge of a Korean file, English ones the payload's");

        // HUXLEY_PATCH_GM_ROOT: a GM's address gets the GM variant of a manifest file under its own version.
        string gmRoot = Path.Combine(root, "gm");
        Directory.CreateDirectory(gmRoot);
        File.WriteAllBytes(Path.Combine(gmRoot, "Map.war"), [5, 5, 5, 5]);
        var withGm = new PatchServer(payload, ["Map.war"], Path.Combine(root, "version3.txt"),
            _loggerFactory.CreateLogger<PatchServer>(), clean, null, gmRoot);
        string playerList = withGm.FullFileList("127.0.0.1"), gmList = withGm.FullFileList("127.0.0.1", gm: true);
        var gmAddress = System.Net.IPAddress.Parse("203.0.113.77");
        bool before = PatchServer.IsGm(gmAddress);
        PatchServer.ReportGm(gmAddress);
        Check(playerList.Contains($"\"Map.war\" 2 {PatchServer.Crc32(new byte[] { 9, 9 }):X}\r\n")
            && gmList.Contains($"\"Map.war\" 4 {PatchServer.Crc32(new byte[] { 5, 5, 5, 5 }):X}\r\n")
            && withGm.VersionFile(gm: true) != withGm.VersionFile()
            && string.CompareOrdinal(withGm.GmVersion(), withGm.CurrentVersion()) > 0
            && !before && PatchServer.IsGm(gmAddress)
            && PatchServer.IsGm(System.Net.IPAddress.Parse("::ffff:203.0.113.77")),
            "patch server serves GM addresses the HUXLEY_PATCH_GM_ROOT variants under a separate version");
        Directory.Delete(root, true);
    }

    private void PlayerLanguageChoice()
    {
        var blocks = PlayerLanguage.ParseKoreanBlocks([
            "2|apnic|20261103|1|19830613|20261102|+1000",
            "apnic|KR|ipv4|1.11.0.0|65536|20100106|allocated",
            "apnic|JP|ipv4|1.0.16.0|4096|20110412|allocated",
            "apnic|KR|ipv6|2001:2d8::|32|20000303|allocated",
            "apnic|KR|ipv4|1.16.0.0|262144|20100224|allocated"]);
        PlayerLanguage.UseKoreanBlocks(blocks);
        var korea = System.Net.IPAddress.Parse("1.19.255.255");
        var japan = System.Net.IPAddress.Parse("1.0.16.5");
        var reportedEnglish = System.Net.IPAddress.Parse("1.11.200.1");
        var reportedKorean = System.Net.IPAddress.Parse("203.0.113.77");
        PatchServer.ReportLanguage(reportedEnglish, korean: false);
        PatchServer.ReportLanguage(reportedKorean.MapToIPv6(), korean: true);
        Check(blocks.Length == 2 && PlayerLanguage.IsKorean(korea) && !PlayerLanguage.IsKorean(japan)
            && !PlayerLanguage.IsKorean(System.Net.IPAddress.Parse("1.20.0.0"))
            && !PlayerLanguage.IsKorean(reportedEnglish) && PlayerLanguage.IsKorean(reportedKorean),
            "player language: the launcher's reported language wins, else a Korean APNIC block means Korean");
        PlayerLanguage.UseKoreanBlocks([]);
    }

    private void PatchPublicAddressAndIdleTimeout()
    {
        var lanAddress = System.Net.IPAddress.Parse("192.168.1.5");
        Check(PatchServer.MmoAddress(null, lanAddress) == "192.168.1.5"
            && PatchServer.MmoAddress(null, lanAddress.MapToIPv6()) == "192.168.1.5"
            && PatchServer.MmoAddress(" 203.0.113.9 ", lanAddress) == "203.0.113.9"
            && PatchServer.MmoAddress(null, null) == "127.0.0.1",
            "patch server advertises HUXLEY_GAME_PUBLIC_IP in m_strMMOIP, else the accepting interface");

        string root = Path.Combine(Path.GetTempPath(), $"huxley-patch-nat-{Environment.ProcessId}");
        Directory.CreateDirectory(Path.Combine(root, "Config"));
        File.WriteAllText(Path.Combine(root, "Config", "Game.ini"), "[HuxleyGame.HuxleySubsystem]\r\nm_strMMOIP=10.0.0.1\r\n");
        string? previousPublicIp = Environment.GetEnvironmentVariable("HUXLEY_GAME_PUBLIC_IP");
        using var server = new PatchServer(root, [@"Config\Game.ini"], Path.Combine(root, "version.txt"),
            _loggerFactory.CreateLogger<PatchServer>()) { IoTimeoutMilliseconds = 300 };
        try
        {
            Environment.SetEnvironmentVariable("HUXLEY_GAME_PUBLIC_IP", "203.0.113.9");
            Check(server.Start(0), "patch server starts on an ephemeral port");
            string Get(string path)
            {
                using var http = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port) { ReceiveTimeout = 5000 };
                var stream = http.GetStream();
                stream.Write(System.Text.Encoding.ASCII.GetBytes($"GET {path} HTTP/1.0\r\n\r\n"));
                using var body = new MemoryStream();
                stream.CopyTo(body);
                return System.Text.Encoding.Latin1.GetString(body.ToArray());
            }
            byte[] publicIni = server.RenderConfig(@"Config\Game.ini", "203.0.113.9")!;
            string fullList = Get(PatchServer.FullDir + "FullFile.txt");
            string ini = Get(PatchServer.FullDir + "Config/Game.ini");
            Check(fullList.Contains($"\"Config\\Game.ini\" {publicIni.Length} {PatchServer.Crc32(publicIni):X}\r\n")
                && ini.EndsWith("m_strMMOIP=203.0.113.9\r\n"),
                "a launcher reaching the patch server over NAT gets the configured public MMO address");

            using var idle = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port) { ReceiveTimeout = 5000 };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int read;
            try { read = idle.GetStream().Read(new byte[1]); }
            catch (IOException) { read = -1; }
            Check(read <= 0 && watch.ElapsedMilliseconds < 4000,
                "patch server drops a connection that never sends its request");

            using var trickle = new System.Net.Sockets.TcpClient("127.0.0.1", server.Port);
            var trickleStream = trickle.GetStream();
            watch.Restart();
            bool dropped = false;
            try
            {
                // One byte every 100 ms never trips the per-read timeout, only the header deadline.
                for (int i = 0; i < 40 && !dropped; i++)
                {
                    trickleStream.WriteByte((byte)'G');
                    Thread.Sleep(100);
                    dropped = trickle.Client.Poll(0, System.Net.Sockets.SelectMode.SelectRead)
                        && trickle.Client.Available == 0;
                }
            }
            catch (IOException) { dropped = true; }
            Check(dropped && watch.ElapsedMilliseconds < 3000,
                "patch server drops a peer that trickles its request headers past the deadline");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HUXLEY_GAME_PUBLIC_IP", previousPublicIp);
            server.Dispose();
            Directory.Delete(root, recursive: true);
        }
    }

    private void SettingsFileParsing()
    {
        Check(Lunarite.World.OnlineStatus.Json(["A", "B"], ["C"], 1, 1793849213)
                == "{\"online\":3,\"sapiens\":2,\"alternative\":1,\"sapiensNames\":[\"A\",\"B\"],\"alternativeNames\":[\"C\"],\"inMission\":1,\"updated\":1793849213}\n",
            "HUXLEY_STATUS_FILE holds players online per faction as one JSON line");
        string korean = System.Text.Encoding.Latin1.GetString(new byte[] { 0xB4, 0xEB, 0xC7, 0xD1 }); // CP949 "대한"
        string western = System.Text.Encoding.Latin1.GetString(new byte[] { 0x43, 0x61, 0x66, 0xE9 }); // CP1252 "Café"
        Check(Lunarite.World.OnlineStatus.DisplayName(korean) == "대한"
            && Lunarite.World.OnlineStatus.DisplayName(western) == "Café"
            && Lunarite.World.OnlineStatus.DisplayName("EdenFPS") == "EdenFPS",
            "status names decode the client's code page: CP949 Korean, else CP1252");
        var parsed = Lunarite.SettingsFile.Parse([
            "# comment", "", "HUXLEY_SERVER_NAME=Test Server", "  HUXLEY_WINE = xvfb-run -a wine ",
            "QUOTED=\"a b\"", "no equals sign", "=no key"]).ToList();
        Check(parsed.SequenceEqual([("HUXLEY_SERVER_NAME", "Test Server"), ("HUXLEY_WINE", "xvfb-run -a wine"), ("QUOTED", "a b")]),
            "huxley.conf lines parse as KEY=VALUE with comments, blanks and quotes handled");

        string path = Path.Combine(Path.GetTempPath(), $"huxley-settings-{Environment.ProcessId}.conf");
        File.WriteAllLines(path, ["HUXLEY_TEST_FROM_FILE=file", "HUXLEY_TEST_ALREADY_SET=file"]);
        Environment.SetEnvironmentVariable("HUXLEY_TEST_ALREADY_SET", "environment");
        try
        {
            int applied = Lunarite.SettingsFile.Apply(path);
            Check(applied == 1 && Environment.GetEnvironmentVariable("HUXLEY_TEST_FROM_FILE") == "file"
                && Environment.GetEnvironmentVariable("HUXLEY_TEST_ALREADY_SET") == "environment",
                "huxley.conf fills unset settings and an environment variable overrides it");
        }
        finally
        {
            Environment.SetEnvironmentVariable("HUXLEY_TEST_FROM_FILE", null);
            Environment.SetEnvironmentVariable("HUXLEY_TEST_ALREADY_SET", null);
            File.Delete(path);
        }
    }
}
