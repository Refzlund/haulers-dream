using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Steamworks;

namespace SetWorkshopTags
{
    /// <summary>
    /// Sets the Steam Workshop display tags on a published item via the Steamworks SetItemTags API.
    /// SetItemTags REPLACES the full tag set, so pass every tag you want.
    ///
    /// Usage:  SetWorkshopTags.exe [publishedFileId] [tag1 tag2 ...]
    /// Default: publishedFileId 3742459652, tags "Mod" "1.6" (Hauler's Dream).
    ///
    /// Requires: the Steam client running and logged into the account that OWNS the item, and
    /// steam_api64.dll + steam_appid.txt (294100) sitting next to this exe. No content is
    /// re-uploaded — this is a metadata-only update.
    /// </summary>
    internal static class Program
    {
        private const uint AppId = 294100; // RimWorld
        private const ulong DefaultFileId = 3742459652UL;
        private static readonly string[] DefaultTags = { "Mod", "1.6" };

        private static volatile bool _done;
        private static SubmitItemUpdateResult_t _result;
        private static bool _ioFailure;
        private static CallResult<SubmitItemUpdateResult_t> _callResult; // keep rooted so the GC can't collect it mid-call

        private static int Main(string[] args)
        {
            ulong fileId = DefaultFileId;
            var tags = new List<string>();
            if (args != null && args.Length > 0 && ulong.TryParse(args[0], out var parsed))
            {
                fileId = parsed;
                for (int i = 1; i < args.Length; i++) tags.Add(args[i]);
            }
            else if (args != null)
            {
                tags.AddRange(args); // all args are tags; keep default file id
            }
            if (tags.Count == 0) tags.AddRange(DefaultTags);

            Console.WriteLine($"Setting tags on item {fileId} (app {AppId}): [{string.Join(", ", tags)}]");

            if (!SteamAPI.Init())
            {
                Console.Error.WriteLine(
                    "ERROR: SteamAPI.Init failed. The Steam client must be running and logged into the account " +
                    "that owns the item, and steam_api64.dll + steam_appid.txt (294100) must sit next to this exe. " +
                    "Closing the RimWorld game (Steam itself stays open) can also help if a same-appid session conflicts.");
                return 2;
            }

            try
            {
                // Init can return before the API session reaches a logged-on state; submitting then yields
                // k_EResultNotLoggedOn. Pump callbacks until the client reports logged-on (or give up).
                var warm = Stopwatch.StartNew();
                while (!SteamUser.BLoggedOn() && warm.Elapsed.TotalSeconds < 15)
                {
                    SteamAPI.RunCallbacks();
                    Thread.Sleep(100);
                }
                if (!SteamUser.BLoggedOn())
                {
                    Console.Error.WriteLine("ERROR: the Steam client is not logged on to Steam (it may be in Offline Mode). " +
                                            "Go online in Steam and re-run.");
                    return 6;
                }
                Console.WriteLine("Steam logged on; submitting tag update...");

                var handle = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(fileId));
                if (!SteamUGC.SetItemTags(handle, tags))
                {
                    Console.Error.WriteLine("ERROR: SetItemTags returned false (a tag may be invalid for this app's allowed tag list).");
                    return 3;
                }

                _callResult = CallResult<SubmitItemUpdateResult_t>.Create(OnSubmitted);
                var call = SteamUGC.SubmitItemUpdate(handle, "Set workshop tags");
                _callResult.Set(call);

                var sw = Stopwatch.StartNew();
                while (!_done && sw.Elapsed.TotalSeconds < 60)
                {
                    SteamAPI.RunCallbacks();
                    Thread.Sleep(100);
                }

                if (!_done)
                {
                    Console.Error.WriteLine("ERROR: timed out (60s) waiting for the SubmitItemUpdate result.");
                    return 4;
                }
                if (_ioFailure)
                {
                    Console.Error.WriteLine("ERROR: IO failure delivering the SubmitItemUpdate result.");
                    return 4;
                }

                Console.WriteLine($"SubmitItemUpdate result: {_result.m_eResult}");
                if (_result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
                    Console.WriteLine("NOTE: the account still needs to accept the Steam Workshop legal agreement for this item " +
                                      "(open the item page and accept) — the tag change may not appear until then.");

                if (_result.m_eResult == EResult.k_EResultOK)
                {
                    Console.WriteLine("OK: tags submitted successfully.");
                    return 0;
                }
                Console.Error.WriteLine($"ERROR: Steam returned {_result.m_eResult}.");
                return 5;
            }
            finally
            {
                SteamAPI.Shutdown();
            }
        }

        private static void OnSubmitted(SubmitItemUpdateResult_t r, bool ioFailure)
        {
            _result = r;
            _ioFailure = ioFailure;
            _done = true;
        }
    }
}
