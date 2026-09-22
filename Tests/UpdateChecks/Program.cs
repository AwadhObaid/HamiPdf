using System.Text.Json;
using HamiPdf.Updates;
string Feed(string tag="v0.12.0", bool draft=false, bool prerelease=false, string? url=null, bool asset=true) => JsonSerializer.Serialize(new {
 tag_name=tag,draft,prerelease,body="إصلاحات وتحسينات",assets=asset?new[]{new{name="HamiPdf-Setup-0.12.0-win-x64.exe",state="uploaded",size=120L,browser_download_url=url??"https://github.com/AwadhObaid/HamiPdf-Releases/releases/download/v0.12.0/HamiPdf-Setup-0.12.0-win-x64.exe"}}:[]});
void Need(bool ok){if(!ok)throw new Exception("Update regression failed");}
void Reject(string json){try{ReleaseFeed.Parse(json,new Version(0,11,0));throw new Exception("Unsafe/incomplete release accepted");}catch(System.IO.InvalidDataException){}}
Need(ReleaseFeed.Parse(Feed(),new Version(0,11,0,0))?.Version==new Version(0,12,0));
Need(ReleaseFeed.Parse(Feed(),new Version(0,12,0,0))==null);
Need(ReleaseFeed.Parse(Feed(),new Version(0,13,0))==null);
Need(ReleaseFeed.Parse(Feed(draft:true),new Version(0,11,0))==null);
Need(ReleaseFeed.Parse(Feed(prerelease:true),new Version(0,11,0))==null);
Reject(Feed(tag:"v0.12.0-beta"));Reject(Feed(url:"https://example.org/setup.exe"));Reject(Feed(url:"http://github.com/AwadhObaid/HamiPdf-Releases/releases/download/v0.12.0/HamiPdf-Setup-0.12.0-win-x64.exe"));Reject(Feed(asset:false));
Need(ReleaseFeed.Parse(Feed(),new Version(0,9,2))?.Notes=="إصلاحات وتحسينات");
Console.WriteLine("PASS: version comparison, stable-only updates, matching official installer URL, incomplete releases rejected, Arabic notes.");
