using System;
using ExternalDisplayGuard;
static class OfflineRunner {
 static int Main(string[] args){int code=OfflineChecks.Run(args.Length>0?args[0]:"offline-results.json");Console.WriteLine(code==0?"Offline checks passed; no native display calls.":"Offline checks failed; inspect the report.");return code;}
}
