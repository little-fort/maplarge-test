using Microsoft.AspNetCore.Mvc;
using System.IO;
using TestProject.Helpers;
using TestProject.Models;

namespace TestProject.Controllers 
{
    [ApiController]
    [Route("[controller]")]
    public class DirectoryController : ControllerBase 
    {

        private readonly ILogger<DirectoryController> _logger;

        public DirectoryController(ILogger<DirectoryController> logger) 
        {
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string path) 
        {
            var app = Environment.CurrentDirectory; //AppDomain.CurrentDomain.BaseDirectory;
            var fullPath = Path.Combine(app, path.TrimStart('/').TrimStart('\\'));
            var directory = string.Empty;
            if (!Directory.Exists(fullPath) && !System.IO.File.Exists(fullPath))
                return NotFound();

            if (System.IO.File.Exists(fullPath))
            {
                // Infer path for directory browser and render file preview
            }

            var dto = new List<ServerObject>();
            var subdirs = Directory.GetDirectories(fullPath);
            foreach (var dir in subdirs)
            {
                var info = new DirectoryInfo(dir);
                dto.Add(new ServerObject
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Type = ServerObjectType.Directory
                });
            }

            var files = Directory.GetFiles(fullPath);
            foreach (var file in files)
            {
                var info = new FileInfo(file);
                dto.Add(new ServerObject
                {
                    Name = info.Name,
                    FullPath = info.FullName,
                    LastWriteTimeUtc = info.LastWriteTimeUtc,
                    Size = info.Length,
                    DisplaySize = FormatHelper.FormatDisplaySize(info.Length),
                    Extension = info.Name.Split('.').ToList().Last().ToUpper(),
                    Type = ServerObjectType.File
                });
            }

            return Ok(new { directory = dto });
        }
    }
}