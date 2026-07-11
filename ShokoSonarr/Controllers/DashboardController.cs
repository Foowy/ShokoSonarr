using System.Reflection;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using IoFile = System.IO.File;

namespace ShokoSonarr.Controllers;

/// <summary>Serves the embedded dashboard page and its static assets.</summary>
[ApiController]
[ApiVersion(ShokoSonarrConstants.ApiVersion)]
[Route(ShokoSonarrConstants.BasePath)]
public class DashboardController : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider s_contentTypeProvider = new();

    // AppContext.BaseDirectory resolves to the host's directory when loaded via the
    // plugin AssemblyLoadContext, not this assembly's own folder — use its location instead.
    private static readonly string s_dashboardDir = Path.Combine(
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? AppContext.BaseDirectory,
        "dashboard");

    /// <summary>Serves the main dashboard page.</summary>
    /// <returns>The dashboard HTML content.</returns>
    [HttpGet("dashboard")]
    public IActionResult GetDashboardPage()
    {
        var indexPath = Path.Combine(s_dashboardDir, "dashboard.html");
        return IoFile.Exists(indexPath) ? Content(IoFile.ReadAllText(indexPath), "text/html") : NotFound();
    }

    /// <summary>Serves static assets (CSS, JS) from the dashboard folder.</summary>
    /// <param name="path">The relative asset path.</param>
    /// <returns>The physical asset file.</returns>
    [HttpGet("dashboard/{*path}")]
    public IActionResult GetAssetFile([FromRoute] string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(s => s is "." or ".." || s.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            return NotFound();

        var requested = Path.GetFullPath(Path.Combine([s_dashboardDir, .. segments]));
        if (!IoFile.Exists(requested))
            return NotFound();

        if (!s_contentTypeProvider.TryGetContentType(requested, out var contentType))
            contentType = "application/octet-stream";
        return PhysicalFile(requested, contentType);
    }
}
