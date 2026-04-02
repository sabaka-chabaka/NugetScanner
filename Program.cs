// 2026 sabaka-chabaka 

using System.Xml.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using Spectre.Console;

AnsiConsole.Write(new FigletText("NuGet Spy").Color(Color.Green)); 
AnsiConsole.MarkupLine("[grey]Status: [/][green]Ready to scan[/]");

var targetPath = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

if (!Directory.Exists(targetPath)) {
    AnsiConsole.MarkupLine($"[red]Error:[/] Path [yellow]{targetPath}[/] not found.");
    return;
}

var projectFiles = Directory.GetFiles(targetPath, "*.csproj", SearchOption.AllDirectories);
if (projectFiles.Length == 0) {
    AnsiConsole.MarkupLine("[red]Error:[/] No .csproj files found.");
    return;
}

using var client = new HttpClient();
client.DefaultRequestHeaders.Add("User-Agent", "NuGetSpy-Tool");

foreach (var path in projectFiles) {
    var table = new Table().Border(TableBorder.Rounded).Expand().Title($"[yellow]{Path.GetFileName(path)}[/]");
    table.AddColumn("Package");
    table.AddColumn("Current", c => c.Centered());
    table.AddColumn("Latest", c => c.Centered());
    table.AddColumn("Vulnerabilities", c => c.Centered()); 
    table.AddColumn("Status");

    var doc = XDocument.Load(path);
    var packages = doc.Descendants("PackageReference")
        .Select(pr => new { 
            Name = pr.Attribute("Include")?.Value, 
            Version = pr.Attribute("Version")?.Value ?? pr.Element("Version")?.Value 
        })
        .Where(x => x.Name != null && x.Version != null).ToList();

    if (!packages.Any()) continue;

    await AnsiConsole.Status().StartAsync("Scanning...", async ctx => {
        foreach (var pkg in packages) {
            ctx.Status($"Checking [bold cyan]{pkg.Name}[/]...");
            
            var latestVersion = await GetLatestVersion(client, pkg.Name!);
            var vulnerabilities = await GetVulnerabilities(client, pkg.Name!, pkg.Version!);
            
            bool isOutdated = latestVersion != "N/A" && latestVersion != "Err" && latestVersion != pkg.Version;
            bool hasVulnerabilities = vulnerabilities > 0;
            
            string vulnStatus = hasVulnerabilities 
                ? $"[bold red]!! {vulnerabilities} Found !![/]" 
                : "[green]Clean[/]";
                
            string status = isOutdated ? "[yellow]Update[/]" : "[green]OK[/]";
            if (hasVulnerabilities) status = "[bold white on red] DANGER [/]";
            if (latestVersion == "Err") status = "[red]API Err[/]";

            table.AddRow(
                pkg.Name!, 
                pkg.Version!, 
                isOutdated ? $"[bold red]{latestVersion}[/]" : latestVersion,
                vulnStatus,
                status);
        }
    });
    AnsiConsole.Write(table);
}

AnsiConsole.MarkupLine("[bold green]Scan finished![/]");

async Task<string> GetLatestVersion(HttpClient httpClient, string packageName)
{
    try {
        var url = $"https://azuresearch-usnc.nuget.org/query?q={packageName}&prerelease=false&take=1";

        var response = await httpClient.GetFromJsonAsync<JsonElement>(url);
        return response.GetProperty("data")[0].GetProperty("version").GetString() ?? "N/A";
    } catch { return "Err"; }
}

async Task<int> GetVulnerabilities(HttpClient httpClient, string packageName, string version)
{
    try {
        var url = $"https://nuget.org{packageName.ToLower()}/{version.ToLower()}.json";
        var response = await httpClient.GetFromJsonAsync<JsonElement>(url);
        
        if (response.TryGetProperty("vulnerabilities", out var vulnArray)) {
            return vulnArray.GetArrayLength();
        }
        return 0;
    } catch { return 0; }
}
