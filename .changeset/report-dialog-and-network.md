---
"haulers-dream": patch
---

Make in-game issue reporting work for players whose system blocks the report connection, and fix the report dialog's clipped error message.

Some players' reports failed with "Unknown Error" because their Unity/Mono TLS stack could not validate the report server's (valid) certificate. Every report request now accepts the certificate for Hauler's Dream's own first-party report endpoint, which restores reporting for those players. This does drop chain validation on those specific requests, so it is worth being clear about what they carry: the tail of your Player.log (which can include local file paths and your OS username), your SteamID64 and Steam persona name, your active mod list, the per-install token that scopes reads to your own reports, and any log or screenshot you chose to attach. The decision still stands because these requests go only to Hauler's Dream's own endpoint, the alternative leaves affected players unable to report at all, and certificate pinning would break on the report host's routine certificate rotation. The handler is scoped to the report requests only, never a global override, so nothing else in the game is affected.

When the connection still fails (for example a firewall or antivirus blocking RimWorld), the error message now says so, in all 15 languages, and the dialog's status area is measured from the actual message so even a long or wrapped translation is no longer clipped to a sliver of red. Also silences two startup warnings about texture-holding classes missing the StaticConstructorOnStartup attribute, which makes the "Remember plan" toggle icon load reliably instead of ever falling back to a magenta placeholder.
